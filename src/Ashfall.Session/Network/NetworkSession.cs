using System.Globalization;
using System.Net;
using Ashfall.Content;
using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Networking.Protocol;
using Ashfall.Networking.Transport;
using Ashfall.Networking.Wire;
using Ashfall.Session.Ai;
using Ashfall.Session.Commands;
using Ashfall.Session.Maps;
using Ashfall.Session.Replays;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Network;

/// <summary>
/// One player's side of a peer-to-peer match (GDD section 8): the lobby, then deterministic lockstep.
/// </summary>
/// <remarks>
/// <para><b>Lobby.</b> The host owns the lobby state and broadcasts it. Joiners say hello to the host,
/// get a slot, then open direct connections to every other player (the one with the smaller id
/// dials), so the full mesh exists before the match starts. The host starts once everyone is ready;
/// every peer then checks that the map it generates hashes the same as the host's.</para>
/// <para><b>Lockstep.</b> A command issued while tick T runs is sent as this seat's turn for tick
/// T + delay, to every peer, every tick (an empty turn still counts). A tick runs only when every
/// human seat's turn for it has arrived; AI seats are computed identically on every machine. Every
/// 30 ticks each peer sends its state hash; any mismatch halts the match and writes a dump.</para>
/// <para><b>Rejoin.</b> A player whose game crashed connects to anyone still playing, receives the
/// match settings and every turn so far, replays them at full speed, reconnects to the others (who
/// resend their recent turns) and carries on. The host has no authority once the match starts.</para>
/// <para>Single-threaded: call <see cref="Update"/> every frame (client) or loop (headless).</para>
/// </remarks>
public sealed class NetworkSession : IDisposable
{
    private readonly ReliableTransport _transport;
    private readonly NetworkOptions _options;
    private readonly HostSettings? _hostSettings;
    private readonly Dictionary<ConnectionId, Link> _links = [];
    private readonly Dictionary<long, ulong> _ownHashes = [];
    private readonly Dictionary<(long Tick, int Seat), ulong> _peerHashes = [];
    private readonly List<string> _events = [];
    private readonly List<Command> _scratch = [];
    private readonly HashSet<string> _dialled = new(StringComparer.Ordinal);

    // Turns, hashes and leaves can overtake the host's start message (they travel on other connections).
    private readonly List<(ConnectionId Link, Message Message)> _early = [];
    private ConnectionId? _entry;
    private StartMessage? _start;
    private ScriptedSkirmishAi? _bot;
    private long _nextOwnTurn;
    private long _catchUpUntil = -1;
    private bool _wantReady;
    private bool _sentReady;
    private bool _rejoining;
    private bool _left;

    private NetworkSession(ReliableTransport transport, NetworkIdentity identity, NetworkOptions options, HostSettings? host)
    {
        _transport = transport;
        _options = options;
        _hostSettings = host;
        Identity = identity;
        IsHost = host is not null;
        ContentHash = GameContent.Standard.ComputeHash();
    }

    public NetworkIdentity Identity { get; }

    public bool IsHost { get; }

    public ulong ContentHash { get; }

    public SessionState State { get; private set; } = SessionState.Connecting;

    /// <summary>Why the session failed, for the user.</summary>
    public string? Error { get; private set; }

    public LobbyState? Lobby { get; private set; }

    public int LocalSlot { get; private set; } = -1;

    /// <summary>This player's seat (player id) in the match, once started.</summary>
    public int LocalSeat { get; private set; } = -1;

    /// <summary>The running match, once started.</summary>
    public HeadlessMatch? Match { get; private set; }

    /// <summary>Where the interface puts this player's commands during the match.</summary>
    public LocalOrders? Orders { get; private set; }

    public int InputDelay => _start?.InputDelay ?? 0;

    /// <summary>Human seats the next tick is waiting for (empty while running smoothly).</summary>
    public IReadOnlyList<int> WaitingFor { get; private set; } = [];

    public DesyncReport? Desync { get; private set; }

    /// <summary>Raised after every simulation tick this session runs (the interface tracks motion and effects).</summary>
    public event Action? Ticked;

    /// <summary>The display name of the player in <paramref name="seat"/>.</summary>
    public string NameOfSeat(int seat) => SeatName(seat);

    /// <summary>Recent events for the log or the lobby screen ("Ana joined", "Bo left").</summary>
    public IReadOnlyList<string> Events => _events;

    /// <summary>True when this peer has a live connection to every other human in the lobby.</summary>
    public bool MeshComplete => Lobby is not null && OtherHumanSlots().All(slot => LinkForSlot(slot) is not null);

    public IPEndPoint LocalEndPoint => _transport.LocalEndPoint;

    /// <summary>Hosts a lobby; this player takes slot 0.</summary>
    public static NetworkSession Host(ReliableTransport transport, NetworkIdentity identity, HostSettings settings, NetworkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(settings);
        transport.AcceptIncoming = true;
        var session = new NetworkSession(transport, identity, options ?? new NetworkOptions(), settings);
        var slotCount = 1 + settings.OtherSlots.Count;
        var slots = new LobbySlot[slotCount];
        for (var i = 0; i < slotCount; i++)
        {
            var team = settings.Teams is { } teams && i < teams.Count ? teams[i] : i;
            slots[i] = i == 0
                ? new LobbySlot(SlotKind.Human, identity.Peer, identity.Name, team, true, string.Empty)
                : settings.OtherSlots[i - 1] switch
                {
                    SlotKind.Ai => LobbySlot.Ai(team),
                    SlotKind.Closed => new LobbySlot(SlotKind.Closed, PeerId.None, string.Empty, team, false, string.Empty),
                    _ => LobbySlot.Open(team),
                };
        }

        session.Lobby = new LobbyState(1, settings.Seed, session.ContentHash, slots) { MapCode = settings.Map?.ToCode() ?? string.Empty };
        session.LocalSlot = 0;
        session.State = SessionState.Lobby;
        session.Note($"Hosting on port {transport.LocalEndPoint.Port}.");
        return session;
    }

    /// <summary>Joins the lobby hosted at <paramref name="host"/>.</summary>
    public static NetworkSession Join(ReliableTransport transport, NetworkIdentity identity, IPEndPoint host, NetworkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(host);
        transport.AcceptIncoming = true;
        var session = new NetworkSession(transport, identity, options ?? new NetworkOptions(), null);
        session._entry = transport.Connect(host);
        session.Note($"Connecting to {host}...");
        return session;
    }

    /// <summary>Rejoins a match already in progress through any player still in it.</summary>
    public static NetworkSession Rejoin(ReliableTransport transport, NetworkIdentity identity, IPEndPoint peer, NetworkOptions? options = null)
    {
        var session = Join(transport, identity, peer, options);
        session._rejoining = true;
        return session;
    }

    /// <summary>Pumps the network, handles messages and runs every tick that is due and complete.</summary>
    public void Update()
    {
        _transport.Update();
        while (_transport.TryDequeue(out var transportEvent))
        {
            try
            {
                Handle(transportEvent);
            }
            catch (ProtocolException error)
            {
                Note($"Bad message from {Describe(transportEvent.Connection)}: {error.Message}");
            }
        }

        ResolveLinks();
        if (State == SessionState.Lobby)
        {
            UpdateLobby();
        }

        if (State == SessionState.Playing)
        {
            RunTicks();
        }

        _transport.Update();
    }

    /// <summary>Marks this player ready (sent once the mesh to every other player is up).</summary>
    public void SetReady(bool ready)
    {
        _wantReady = ready;
        _sentReady = !ready && _sentReady;
        if (IsHost && Lobby is not null)
        {
            UpdateSlot(LocalSlot, slot => slot with { Ready = ready });
        }
        else if (!ready && _entry is { } host)
        {
            Send(host, new ReadyMessage(false));
        }
    }

    /// <summary>Asks to play on <paramref name="team"/>.</summary>
    public void SetTeam(int team)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(team);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(team, LobbyState.MaxSlots - 1);
        if (IsHost && Lobby is not null)
        {
            UpdateSlot(LocalSlot, slot => slot with { Team = team });
        }
        else if (_entry is { } host)
        {
            Send(host, new SetTeamMessage(team));
        }
    }

    /// <summary>Host: changes an AI or open slot's team.</summary>
    public void SetSlotTeam(int slot, int team)
    {
        if (IsHost && Lobby is not null && slot >= 0 && slot < Lobby.Slots.Count)
        {
            UpdateSlot(slot, s => s with { Team = team });
        }
    }

    /// <summary>Why the host cannot start yet, or null when it can.</summary>
    public string? StartBlocker
    {
        get
        {
            if (!IsHost || Lobby is null || State != SessionState.Lobby)
            {
                return "only the host starts the match";
            }

            var seats = Lobby.Seats().ToList();
            if (seats.Count < 2)
            {
                return "need at least two players";
            }

            if (seats.Count > SkirmishMaps.MaxPlayers)
            {
                return $"at most {SkirmishMaps.MaxPlayers} players";
            }

            if (seats.Select(seat => seat.Seat.Team).Distinct().Count() < 2)
            {
                return "need at least two teams";
            }

            var notReady = seats.FirstOrDefault(seat => seat.Seat.Kind == SlotKind.Human && !seat.Seat.Ready);
            return notReady.Seat is not null ? $"waiting for {notReady.Seat.Name}" : null;
        }
    }

    /// <summary>Host: starts the match for everyone.</summary>
    public void StartMatch()
    {
        if (StartBlocker is { } reason)
        {
            throw new InvalidOperationException("Cannot start: " + reason + ".");
        }

        var lobby = Lobby!;
        var delay = _hostSettings!.InputDelay ?? ChooseInputDelay();
        var start = new StartMessage(lobby, delay, MatchMap.HashOf(MapOf(lobby), lobby.Seats().Count()));
        foreach (var link in _links.Values.Where(link => link.Slot >= 0))
        {
            Send(link.Id, start);
        }

        Begin(start);
    }

    /// <summary>Leaves: in a match, this seat's turns are empty from the next one on, for everyone.</summary>
    public void Leave()
    {
        if (_left)
        {
            return;
        }

        _left = true;
        if (State == SessionState.Playing && LocalSeat >= 0)
        {
            var leave = new LeaveMessage(LocalSeat, _nextOwnTurn - 1);
            Match!.Turns!.SetLeft(LocalSeat, _nextOwnTurn - 1);
            Broadcast(leave);
        }

        // Keep the connections open (keep calling Update) so the goodbye and our last turns are delivered;
        // Dispose closes them.
        State = SessionState.Ended;
        Note("You left.");
    }

    public void Say(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Broadcast(new ChatMessage(LocalSlot, text));
        Note($"{Identity.Name}: {text}");
    }

    /// <summary>The replay of this match so far.</summary>
    public Replay? CaptureReplay() => Match is null ? null : Replay.From(Match);

    public TimeSpan? RoundTripToSlot(int slot) => LinkForSlot(slot) is { } link ? _transport.RoundTrip(link.Id) : null;

    public void Dispose()
    {
        foreach (var link in _links.Values.ToList())
        {
            _transport.Disconnect(link.Id);
        }

        _transport.Dispose();
    }

    // ---- events ---------------------------------------------------------------------------------

    private void Handle(TransportEvent transportEvent)
    {
        var id = transportEvent.Connection;
        switch (transportEvent.Kind)
        {
            case TransportEventKind.Connected:
                OnConnected(id);
                break;

            case TransportEventKind.Disconnected:
                OnDisconnected(id);
                break;

            case TransportEventKind.Message:
                OnMessage(id, Message.Decode(transportEvent.Data));
                break;
        }
    }

    private void OnConnected(ConnectionId id)
    {
        var link = _links.TryGetValue(id, out var existing) ? existing : new Link(id);
        _links[id] = link;
        if (_entry == id)
        {
            link.IsEntry = true;
            Send(id, new HelloMessage(Message.ProtocolVersion, Identity.Peer, Identity.Name, ContentHash, AdvertisedPort, _rejoining));
        }
    }

    private void OnDisconnected(ConnectionId id)
    {
        if (!_links.Remove(id, out var link))
        {
            if (_entry == id && State == SessionState.Connecting)
            {
                Fail("Could not reach the host.");
            }

            return;
        }

        var name = link.Slot >= 0 && Lobby is not null ? Lobby.Slots[link.Slot].Name : link.Peer.ToString();
        if (link.Address is not null)
        {
            _dialled.Remove(link.Address);
        }

        switch (State)
        {
            case SessionState.Lobby when !IsHost && link.IsEntry:
                Fail("Lost the connection to the host.");
                break;

            case SessionState.Lobby when IsHost && link.Slot >= 0:
                UpdateSlot(link.Slot, _ => LobbySlot.Open(Lobby!.Slots[link.Slot].Team));
                Note($"{name} left the lobby.");
                break;

            case SessionState.Connecting:
                Fail("The connection closed before joining.");
                break;

            case SessionState.Playing:
                Note($"Lost connection to {name}; waiting for them to rejoin.");
                break;
        }
    }

    private void OnMessage(ConnectionId id, Message message)
    {
        if (!_links.TryGetValue(id, out var link))
        {
            return;
        }

        switch (message)
        {
            case HelloMessage hello:
                OnHello(link, hello);
                break;
            case WelcomeMessage welcome when link.IsEntry:
                LocalSlot = welcome.Slot;
                State = SessionState.Lobby;
                link.Slot = 0;
                Note("Joined the lobby.");
                break;
            case RejectMessage reject when link.IsEntry:
                Fail("The host refused: " + reject.Reason);
                break;
            case LobbyMessage lobby when link.IsEntry && !IsHost:
                if (Lobby is null || lobby.Lobby.Revision > Lobby.Revision)
                {
                    Lobby = lobby.Lobby;
                }

                break;
            case ReadyMessage ready when IsHost && link.Slot > 0 && State == SessionState.Lobby:
                UpdateSlot(link.Slot, slot => slot with { Ready = ready.Ready });
                break;
            case SetTeamMessage team when IsHost && link.Slot > 0 && State == SessionState.Lobby && team.Team is >= 0 and < LobbyState.MaxSlots:
                UpdateSlot(link.Slot, slot => slot with { Team = team.Team });
                break;
            case StartMessage start when link.IsEntry && State == SessionState.Lobby:
                Begin(start);
                break;
            case PeerHelloMessage peer:
                OnPeerHello(link, peer);
                break;
            case CatchUpMessage catchUp when _rejoining && State == SessionState.Connecting:
                OnCatchUp(link, catchUp);
                break;
            case TurnMessage or HashMessage or LeaveMessage when Match is null:
                _early.Add((link.Id, message));
                break;
            case TurnMessage turn:
                OnTurn(link, turn);
                break;
            case HashMessage hash when SeatOf(link) == hash.Seat:
                _peerHashes[(hash.Tick, hash.Seat)] = hash.Hash;
                CheckHashes(hash.Tick);
                break;
            case LeaveMessage leave when SeatOf(link) == leave.Seat && Match?.Turns is { } turns:
                turns.SetLeft(leave.Seat, leave.LastTurn);
                Note($"{SeatName(leave.Seat)} left the match.");
                break;
            case ChatMessage chat when link.Slot >= 0 && Lobby is not null:
                Note($"{Lobby.Slots[link.Slot].Name}: {chat.Text}");
                break;
        }
    }

    private void OnHello(Link link, HelloMessage hello)
    {
        link.Peer = hello.Peer;
        if (hello.Version != Message.ProtocolVersion)
        {
            Reject(link, $"protocol version {hello.Version} does not match {Message.ProtocolVersion}");
            return;
        }

        if (hello.ContentHash != ContentHash)
        {
            Reject(link, "different game content (another version or mod)");
            return;
        }

        if (hello.Rejoin)
        {
            OnRejoinRequest(link, hello);
            return;
        }

        if (!IsHost || State != SessionState.Lobby || Lobby is null)
        {
            Reject(link, "the match has already started");
            return;
        }

        var slot = Lobby.SlotOf(hello.Peer);
        if (slot < 0)
        {
            slot = Lobby.Slots.Select((s, i) => (s, i)).FirstOrDefault(pair => pair.s.Kind == SlotKind.Open, (null!, -1)).Item2;
        }

        if (slot < 0)
        {
            Reject(link, "the lobby is full");
            return;
        }

        var address = $"{_transport.RemoteEndPoint(link.Id)!.Address}:{hello.ListenPort}";
        link.Slot = slot;
        link.Introduced = true;
        var team = Lobby.Slots[slot].Team;
        UpdateSlot(slot, _ => new LobbySlot(SlotKind.Human, hello.Peer, hello.Name, team, false, address));
        Send(link.Id, new WelcomeMessage(slot));
        Send(link.Id, new LobbyMessage(Lobby));
        Note($"{hello.Name} joined.");
    }

    private void OnRejoinRequest(Link link, HelloMessage hello)
    {
        if (State != SessionState.Playing || Lobby is null || _start is null || Match?.Turns is not { } turns)
        {
            Reject(link, "there is no match in progress to rejoin");
            return;
        }

        var slot = Lobby.SlotOf(hello.Peer);
        var seat = slot < 0 ? -1 : Lobby.SeatOfSlot(slot);
        if (seat < 0 || turns.LeftAfter(seat) is not null)
        {
            Reject(link, "you are not a player in this match");
            return;
        }

        DropStaleLinks(link, slot);
        link.Slot = slot;
        link.Introduced = true;
        var all = turns.All().Select(turn => new TurnMessage(turn.Tick, turn.Seat, turn.Commands)).ToList();
        var leaves = turns.Seats
            .Where(s => turns.LeftAfter(s) is not null)
            .Select(s => new LeaveMessage(s, turns.LeftAfter(s)!.Value))
            .ToList();
        Send(link.Id, new PeerHelloMessage(Identity.Peer, LocalSlot, -1));
        Send(link.Id, new CatchUpMessage(_start with { Lobby = WithKnownAddresses(_start.Lobby) }, seat, all, leaves));
        Note($"{hello.Name} is rejoining.");
    }

    private void OnPeerHello(Link link, PeerHelloMessage hello)
    {
        link.Peer = hello.Peer;
        link.Introduced = true;
        var known = Lobby is not null && hello.Seat >= 0 && hello.Seat < Lobby.Slots.Count && Lobby.Slots[hello.Seat].Peer == hello.Peer;
        if (known || (_rejoining && Lobby is null))
        {
            DropStaleLinks(link, hello.Seat);
            link.Slot = hello.Seat;
        }

        // (Otherwise they joined after our last lobby update; ResolveLinks matches them up when it arrives.)

        // A rejoining peer missed our turns from ResumeFrom on: send them again.
        if (known && hello.ResumeFrom >= 0 && State == SessionState.Playing && Match?.Turns is { } turns && LocalSeat >= 0)
        {
            for (var tick = Math.Max(hello.ResumeFrom, InputDelay); tick < _nextOwnTurn; tick++)
            {
                if (turns.Get(tick, LocalSeat) is { } commands)
                {
                    Send(link.Id, new TurnMessage(tick, LocalSeat, commands));
                }
            }

            Note($"{SeatName(SeatOf(link))} is back.");
        }

        // They dialled us: answer, so both ends know who is on the line.
        if (!link.Outgoing && !link.Answered)
        {
            link.Answered = true;
            Send(link.Id, new PeerHelloMessage(Identity.Peer, LocalSlot, -1));
        }
    }

    private void DropStaleLinks(Link current, int slot)
    {
        foreach (var stale in _links.Values.Where(other => other != current && other.Slot == slot).ToList())
        {
            _links.Remove(stale.Id);
            _transport.Disconnect(stale.Id);
        }
    }

    // The host's own address is not in the lobby (it does not know how others reach it); fill in every
    // address we know from live connections so a rejoining player can dial everyone, host included.
    private LobbyState WithKnownAddresses(LobbyState lobby)
    {
        var slots = lobby.Slots.ToArray();
        for (var slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot].Kind == SlotKind.Human && string.IsNullOrEmpty(slots[slot].Address)
                && LinkForSlot(slot) is { } link && _transport.RemoteEndPoint(link.Id) is { } endPoint)
            {
                slots[slot] = slots[slot] with { Address = endPoint.ToString() };
            }
        }

        return lobby with { Slots = slots };
    }

    // Peers who introduced themselves before our copy of the lobby listed them get their slot now.
    private void ResolveLinks()
    {
        if (Lobby is null)
        {
            return;
        }

        foreach (var link in _links.Values)
        {
            if (link.Slot < 0 && !link.Peer.IsNone && link.Introduced)
            {
                link.Slot = Lobby.SlotOf(link.Peer);
            }
        }
    }

    private void OnCatchUp(Link link, CatchUpMessage catchUp)
    {
        Begin(catchUp.Start);
        if (State != SessionState.Playing || Match?.Turns is not { } turns)
        {
            return;
        }

        foreach (var turn in catchUp.Turns)
        {
            turns.Set(turn.Tick, turn.Seat, [.. turn.Commands]);
        }

        foreach (var leave in catchUp.Leaves)
        {
            turns.SetLeft(leave.Seat, leave.LastTurn);
        }

        _nextOwnTurn = turns.LastTurn(LocalSeat) + 1;
        var firstMissing = 0L;
        while (turns.IsComplete(firstMissing))
        {
            firstMissing++;
        }

        _catchUpUntil = firstMissing;
        _rejoining = false;

        // Reconnect to everyone else, asking each to resend its turns from where ours stop.
        foreach (var slot in OtherHumanSlots())
        {
            if (LinkForSlot(slot) is not null)
            {
                continue;
            }

            var seat = Lobby!.SeatOfSlot(slot);
            Dial(slot, turns.LastTurn(seat) + 1);
        }

        Note($"Rejoined at tick {firstMissing}; catching up.");
        _ = link;
    }

    private void OnTurn(Link link, TurnMessage turn)
    {
        if (Match?.Turns is not { } turns || SeatOf(link) != turn.Seat)
        {
            return; // a peer may only send its own seat's turns
        }

        if (!turns.Set(turn.Tick, turn.Seat, [.. turn.Commands]))
        {
            Note($"{SeatName(turn.Seat)} sent two different turns for tick {turn.Tick}.");
        }
    }

    // ---- lobby ----------------------------------------------------------------------------------

    private void UpdateLobby()
    {
        if (Lobby is null)
        {
            return;
        }

        if (!IsHost)
        {
            // The peer with the smaller id dials, so no pair connects twice.
            foreach (var slot in OtherHumanSlots())
            {
                var other = Lobby.Slots[slot];
                if (slot != 0 && LinkForSlot(slot) is null && string.CompareOrdinal(Identity.Peer.ToHex(), other.Peer.ToHex()) < 0)
                {
                    Dial(slot, -1);
                }
            }
        }

        if (_options.AutoReady && !_wantReady && MeshComplete)
        {
            SetReady(true);
        }

        if (!IsHost && _wantReady && !_sentReady && MeshComplete && _entry is { } host)
        {
            Send(host, new ReadyMessage(true));
            _sentReady = true;
        }
    }

    private void Dial(int slot, long resumeFrom)
    {
        var address = Lobby!.Slots[slot].Address;
        if (string.IsNullOrEmpty(address) || !_dialled.Add(address) || !IPEndPoint.TryParse(address, out var endPoint))
        {
            return;
        }

        var id = _transport.Connect(endPoint);
        _links[id] = new Link(id) { Outgoing = true, Slot = slot, Address = address };

        // Queued now, so it goes first on the connection, ahead of any turns we send them
        // (asking for the turns we missed when rejoining).
        Send(id, new PeerHelloMessage(Identity.Peer, LocalSlot, resumeFrom));
    }

    private void UpdateSlot(int slot, Func<LobbySlot, LobbySlot> change)
    {
        Lobby = Lobby!.WithSlot(slot, change(Lobby.Slots[slot]));
        if (IsHost)
        {
            var message = new LobbyMessage(Lobby);
            foreach (var link in _links.Values.Where(link => link.Slot > 0))
            {
                Send(link.Id, message);
            }
        }
    }

    private void Reject(Link link, string reason)
    {
        Send(link.Id, new RejectMessage(reason));
        Note($"Refused {link.Peer}: {reason}.");
    }

    private int ChooseInputDelay()
    {
        // Half the worst round trip, plus a tick of slack, in ticks; 2..6 (133–400 ms).
        var worst = _links.Values.Select(link => _transport.RoundTrip(link.Id) ?? TimeSpan.Zero).DefaultIfEmpty(TimeSpan.Zero).Max();
        var tick = TimeSpan.FromSeconds(1.0 / SimulationConstants.TicksPerSecond);
        var ticks = (int)Math.Ceiling((worst / 2) / tick) + 1;
        return Math.Clamp(ticks, 2, 6);
    }

    // ---- match ----------------------------------------------------------------------------------

    private void Begin(StartMessage start)
    {
        var lobby = start.Lobby;
        var seats = lobby.Seats().ToList();
        if (seats.Count < 2 || seats.Count > SkirmishMaps.MaxPlayers)
        {
            Fail($"The host started with {seats.Count} players.");
            return;
        }

        if (lobby.MapCode.Length > 0 && !MapSettings.TryParseCode(lobby.MapCode, out _))
        {
            Fail($"The host's map {lobby.MapCode} is unknown to this build (version mismatch).");
            return;
        }

        var map = MapOf(lobby);
        if (MatchMap.HashOf(map, seats.Count) != start.MapHash)
        {
            Fail("This build generates a different map from the host's (version mismatch).");
            return;
        }

        var spec = (MatchSpec.Skirmish(lobby.Seed, seats.Count) with
        {
            Controllers = seats.Select(seat => seat.Seat.Kind == SlotKind.Human ? PlayerController.Remote : PlayerController.ScriptedAi).ToArray(),
            Teams = seats.Select(seat => seat.Seat.Team).ToArray(),
            InputDelay = start.InputDelay,
        }).WithGeneratedMap(map);

        Lobby = lobby;
        _start = start;
        LocalSlot = lobby.SlotOf(Identity.Peer);
        LocalSeat = lobby.SeatOfSlot(LocalSlot);
        Match = HeadlessMatch.Start(spec, _options.Speed, _options.Time);
        Orders = LocalSeat >= 0 ? new LocalOrders(LocalSeat) : null;
        _bot = _options.Bot && LocalSeat >= 0 ? new ScriptedSkirmishAi(LocalSeat, lobby.Seed) : null;
        _nextOwnTurn = start.InputDelay;
        State = SessionState.Playing;
        Note($"Match started: {seats.Count} players, input delay {start.InputDelay} ticks.");

        // Now that there is a match, handle what arrived ahead of the start.
        var early = _early.ToList();
        _early.Clear();
        foreach (var (id, message) in early)
        {
            OnMessage(id, message);
        }
    }

    private void RunTicks()
    {
        var match = Match!;
        var turns = match.Turns!;
        for (var ran = 0; ran < _options.MaxTicksPerUpdate || match.Tick < _catchUpUntil; ran++)
        {
            if (match.IsOver)
            {
                State = SessionState.Ended;
                WaitingFor = [];
                Note(match.WinningTeam >= 0 ? $"Team {match.WinningTeam} wins." : "Draw.");
                return;
            }

            var tick = match.Tick;
            if (!turns.IsComplete(tick))
            {
                WaitingFor = [.. turns.Missing(tick)];
                return;
            }

            var catchingUp = tick < _catchUpUntil;
            if (!catchingUp && match.Clock.TimeUntilDue(tick) > TimeSpan.Zero)
            {
                break;
            }

            ProduceOwnTurns(tick + InputDelay);
            match.Step();
            Ticked?.Invoke();
            if (match.Tick % SimulationConstants.HashCheckInterval == 0)
            {
                PublishHash(match.Tick, match.Simulation.ComputeStateHash());
            }

            if (State != SessionState.Playing)
            {
                return;
            }

            if (catchingUp && match.Tick == _catchUpUntil)
            {
                match.SetSpeed(match.Clock.Speed);
                Note("Caught up.");
            }
        }

        WaitingFor = [];
    }

    private void ProduceOwnTurns(long upTo)
    {
        if (LocalSeat < 0 || _left)
        {
            return;
        }

        var match = Match!;
        var turns = match.Turns!;
        if (turns.LeftAfter(LocalSeat) is not null)
        {
            return;
        }

        _scratch.Clear();
        Orders?.CollectCommands(match.Simulation, match.Tick, _scratch);
        _bot?.CollectCommands(match.Simulation, match.Tick, _scratch);
        _scratch.RemoveAll(command => command.PlayerId != LocalSeat);

        while (_nextOwnTurn <= upTo)
        {
            // Commands go in the first turn produced; any catch-up turns before it are empty.
            Command[] commands = _nextOwnTurn == upTo ? [.. _scratch] : [];
            turns.Set(_nextOwnTurn, LocalSeat, commands);
            Broadcast(new TurnMessage(_nextOwnTurn, LocalSeat, commands));
            _nextOwnTurn++;
        }
    }

    private void PublishHash(long tick, ulong hash)
    {
        _ownHashes[tick] = hash;
        if (LocalSeat >= 0)
        {
            Broadcast(new HashMessage(tick, LocalSeat, hash));
        }

        CheckHashes(tick);
    }

    private void CheckHashes(long tick)
    {
        if (!_ownHashes.TryGetValue(tick, out var own) || State != SessionState.Playing)
        {
            return;
        }

        var hashes = new SortedDictionary<int, ulong>();
        if (LocalSeat >= 0)
        {
            hashes[LocalSeat] = own;
        }

        var mismatch = false;
        foreach (var seat in Match!.Turns!.Seats)
        {
            if (_peerHashes.TryGetValue((tick, seat), out var theirs))
            {
                hashes[seat] = theirs;
                mismatch |= theirs != own;
            }
        }

        if (!mismatch)
        {
            return;
        }

        State = SessionState.Desynced;
        var dump = WriteDump(tick, hashes);
        Desync = new DesyncReport(tick, hashes, dump);
        Note($"DESYNC at tick {tick}: " + string.Join(", ", hashes.Select(pair => $"{SeatName(pair.Key)} {StateHasher.Format(pair.Value)}")));
    }

    private string? WriteDump(long tick, IReadOnlyDictionary<int, ulong> hashes)
    {
        if (_options.DumpDirectory is not { } directory)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        var name = string.Create(CultureInfo.InvariantCulture, $"desync-tick{tick}-seat{LocalSeat}-{Identity.Peer}");
        var path = Path.Combine(directory, name + ".txt");
        var header = string.Join('\n', hashes.Select(pair => string.Create(CultureInfo.InvariantCulture, $"seat {pair.Key} ({SeatName(pair.Key)}): {StateHasher.Format(pair.Value)}")));
        File.WriteAllText(path, $"Ashfall desync dump\npeer {Identity.Peer} seat {LocalSeat}\nmismatch at tick {tick}\n{header}\n\n# state now\n{StateDump.Describe(Match!.Simulation)}");
        Replay.From(Match).Save(Path.Combine(directory, name + ".ashreplay"));
        return path;
    }

    // ---- helpers --------------------------------------------------------------------------------

    private IEnumerable<int> OtherHumanSlots()
    {
        if (Lobby is null)
        {
            yield break;
        }

        for (var slot = 0; slot < Lobby.Slots.Count; slot++)
        {
            if (slot != LocalSlot && Lobby.Slots[slot].Kind == SlotKind.Human && !Lobby.Slots[slot].Peer.IsNone)
            {
                yield return slot;
            }
        }
    }

    private Link? LinkForSlot(int slot) =>
        _links.Values.FirstOrDefault(link => link.Slot == slot && _transport.IsConnected(link.Id) && (link.IsEntry || link.Introduced || link.Outgoing));

    private int SeatOf(Link link) => link.Slot >= 0 && Lobby is not null ? Lobby.SeatOfSlot(link.Slot) : -1;

    private string SeatName(int seat)
    {
        if (Lobby is null || seat < 0)
        {
            return "?";
        }

        foreach (var (slot, info) in Lobby.Seats())
        {
            if (Lobby.SeatOfSlot(slot) == seat)
            {
                return info.Name;
            }
        }

        return "?";
    }

    private int AdvertisedPort => _options.AdvertisedPort ?? _transport.LocalEndPoint.Port;

    /// <summary>The generated map a lobby plays on, or null for the hand-made map (or an unknown code).</summary>
    public static MapSettings? MapOf(LobbyState? lobby) =>
        lobby is not null && lobby.MapCode.Length > 0 && MapSettings.TryParseCode(lobby.MapCode, out var settings) ? settings : null;

    private void Broadcast(Message message)
    {
        var bytes = message.Encode();
        foreach (var link in _links.Values)
        {
            if (link.Slot >= 0 && link.Slot != LocalSlot)
            {
                _transport.Send(link.Id, bytes);
            }
        }
    }

    private void Send(ConnectionId id, Message message) => _transport.Send(id, message.Encode());

    private string Describe(ConnectionId id) => _links.TryGetValue(id, out var link) && !link.Peer.IsNone ? link.Peer.ToString() : id.ToString();

    private void Fail(string reason)
    {
        Error = reason;
        State = SessionState.Failed;
        Note(reason);
    }

    private void Note(string text)
    {
        _events.Add(text);
        if (_events.Count > 200)
        {
            _events.RemoveRange(0, 100);
        }
    }

    private sealed class Link(ConnectionId id)
    {
        public ConnectionId Id { get; } = id;

        public PeerId Peer { get; set; }

        /// <summary>The remote player's lobby slot, once known (-1 before they say hello).</summary>
        public int Slot { get; set; } = -1;

        /// <summary>The connection this peer opened to the host (or the rejoin contact).</summary>
        public bool IsEntry { get; set; }

        /// <summary>We dialled this mesh connection.</summary>
        public bool Outgoing { get; init; }

        /// <summary>The peer has said who it is (hello or peer hello).</summary>
        public bool Introduced { get; set; }

        /// <summary>We have introduced ourselves on a connection they dialled.</summary>
        public bool Answered { get; set; }

        public string? Address { get; init; }

    }
}
