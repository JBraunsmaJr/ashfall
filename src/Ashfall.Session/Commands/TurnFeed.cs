using Ashfall.Simulation;
using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Commands;

/// <summary>
/// Commands for the <see cref="PlayerController.Remote"/> seats, one turn per seat per tick: what
/// arrives over the network in a lockstep match, or what a replay recorded. A tick may only run once
/// every remote seat's turn for it is here (<see cref="IsComplete"/>). Turns before
/// <see cref="InputDelay"/> are empty by definition, as are a seat's turns after it has left.
/// </summary>
public sealed class TurnFeed : ICommandSource
{
    private readonly Dictionary<long, Command[]>[] _turns;
    private readonly long[] _leftAfter;

    /// <param name="seats">The seats fed from turns (all seats in a replay; the human seats in a network match).</param>
    /// <param name="players">Total players in the match.</param>
    public TurnFeed(IReadOnlyList<int> seats, int players, int inputDelay = 0)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentOutOfRangeException.ThrowIfNegative(inputDelay);
        Seats = [.. seats.Order()];
        InputDelay = inputDelay;
        _turns = new Dictionary<long, Command[]>[players];
        _leftAfter = new long[players];
        for (var i = 0; i < players; i++)
        {
            _turns[i] = [];
            _leftAfter[i] = long.MaxValue;
        }
    }

    public IReadOnlyList<int> Seats { get; }

    public int InputDelay { get; }

    public bool IsFed(int seat) => Seats.Contains(seat);

    /// <summary>Stores a seat's turn. Returns false if a different turn for that tick was already stored.</summary>
    public bool Set(long tick, int seat, Command[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (!IsFed(seat) || tick < InputDelay)
        {
            return false;
        }

        if (_turns[seat].TryGetValue(tick, out var existing))
        {
            return existing.AsSpan().SequenceEqual(commands);
        }

        _turns[seat][tick] = commands;
        return true;
    }

    public bool Has(long tick, int seat) =>
        tick < InputDelay || tick > _leftAfter[seat] || _turns[seat].ContainsKey(tick);

    /// <summary>The stored turn, empty when implicitly empty, or null while it has not arrived.</summary>
    public Command[]? Get(long tick, int seat) =>
        tick < InputDelay || tick > _leftAfter[seat] ? []
        : _turns[seat].TryGetValue(tick, out var commands) ? commands
        : null;

    /// <summary>The seat's turns are empty from <paramref name="lastTurn"/> + 1 on (it left the match).</summary>
    public void SetLeft(int seat, long lastTurn) => _leftAfter[seat] = Math.Min(_leftAfter[seat], lastTurn);

    public long? LeftAfter(int seat) => _leftAfter[seat] == long.MaxValue ? null : _leftAfter[seat];

    public bool IsComplete(long tick)
    {
        foreach (var seat in Seats)
        {
            if (!Has(tick, seat))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Seats whose turn for <paramref name="tick"/> is missing.</summary>
    public IEnumerable<int> Missing(long tick) => Seats.Where(seat => !Has(tick, seat));

    /// <summary>The last tick with an explicitly stored turn for the seat, or <see cref="InputDelay"/> - 1.</summary>
    public long LastTurn(int seat)
    {
        var last = (long)InputDelay - 1;
        foreach (var tick in _turns[seat].Keys.Order())
        {
            last = Math.Max(last, tick);
        }

        return last;
    }

    /// <summary>Every stored turn, ordered by tick then seat.</summary>
    public IEnumerable<(long Tick, int Seat, Command[] Commands)> All() =>
        Seats.SelectMany(seat => _turns[seat].Select(pair => (pair.Key, seat, pair.Value)))
            .OrderBy(turn => turn.Key).ThenBy(turn => turn.seat);

    public void CollectCommands(GameSimulation simulation, long tick, List<Command> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var seat in Seats)
        {
            var commands = Get(tick, seat)
                ?? throw new InvalidOperationException($"Tick {tick} ran without seat {seat}'s turn.");
            output.AddRange(commands);
        }
    }
}
