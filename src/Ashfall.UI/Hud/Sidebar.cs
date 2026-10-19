using System.Globalization;
using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Orders;
using Ashfall.Simulation.Players;
using Ashfall.UI.Drawing;
using Ashfall.UI.Text;

namespace Ashfall.UI.Hud;

/// <summary>
/// The classic right-hand sidebar (GDD section 5): credits, power bar, radar, then build tabs (base, defence,
/// infantry, vehicles) over a grid of cameos, one per structure or unit in that tab. Cameos show an icon, cost,
/// progress and queue length, and grey out with a reason when prerequisites are missing. A tab whose cameos have
/// just become available, or that has something building, is marked so nothing ready goes unnoticed.
/// </summary>
public sealed class Sidebar
{
    private static readonly SidebarTab[] Tabs = Enum.GetValues<SidebarTab>();

    private readonly List<SidebarEntry> _entries = [];
    private readonly int[] _availableLastUpdate = new int[Tabs.Length];
    private readonly bool[] _attention = new bool[Tabs.Length];
    private int _faction = -1;
    private SidebarTab _activeTab;

    public IReadOnlyList<SidebarEntry> Entries => _entries;

    public SidebarLayout Layout { get; private set; }

    /// <summary>The tab whose cameos are shown. Choosing a tab clears its attention marker.</summary>
    public SidebarTab ActiveTab
    {
        get => _activeTab;
        set
        {
            _activeTab = value;
            _attention[(int)value] = false;
        }
    }

    /// <summary>Whether the stance buttons show (armed units of the player's are selected). Set by the screen.</summary>
    public bool ShowStances { get; set; }

    /// <summary>The selection's stance when all of it shares one; null when mixed. Set by the screen.</summary>
    public UnitStance? CurrentStance { get; set; }

    /// <summary>Whether the repair button shows (the player's structures are selected, and no armed units). Set by the screen.</summary>
    public bool ShowRepair { get; set; }

    /// <summary>Whether every selected structure is being repaired already. Set by the screen.</summary>
    public bool RepairOn { get; set; }

    /// <summary>Whether the unload button shows beside repair (a selected bunker has people in it). Set by the screen.</summary>
    public bool ShowUnload { get; set; }

    /// <summary>Where the repair button sits: the stance row's place, full width (half when unload shows too).</summary>
    public (float X, float Y, float Width, float Height) RepairRect()
    {
        var (_, y, _, height) = StanceRect(UnitStance.Guard);
        var width = Layout.Width - (2 * Layout.Padding);
        return (Layout.X + Layout.Padding, y, ShowUnload ? (width / 2f) - (2 * Layout.Scale) : width, height);
    }

    /// <summary>Where the unload button sits: the right half of the row.</summary>
    public (float X, float Y, float Width, float Height) UnloadRect()
    {
        var (x, y, width, height) = RepairRect();
        return (x + width + (4 * Layout.Scale), y, width, height);
    }

    /// <summary>True when <paramref name="point"/> is on the unload button and it shows.</summary>
    public bool HitUnload(Vector2 point) => ShowRepair && ShowUnload && Inside(point, UnloadRect());

    /// <summary>True when <paramref name="point"/> is on the repair button and it shows.</summary>
    public bool HitRepair(Vector2 point) => ShowRepair && Inside(point, RepairRect());

    private void DrawRepair(Canvas canvas, SidebarLayout layout, Vector2 mouse)
    {
        if (!ShowRepair)
        {
            return;
        }

        var (x, y, width, height) = RepairRect();
        var hovered = Inside(mouse, (x, y, width, height));
        canvas.Rect(x, y, width, height, RepairOn ? Palette.ButtonActive : hovered ? Palette.ButtonHover : Palette.Button);
        canvas.TextCentered(x + (width / 2f), y + ((height - (PixelFont.GlyphHeight * layout.Scale)) / 2f), RepairOn ? "REPAIRING (R)" : "REPAIR (R)",
            layout.Scale, RepairOn ? Palette.TextGood : Palette.Text);
        if (RepairOn)
        {
            canvas.Frame(x, y, width, height, layout.Scale, Palette.TextGood);
        }

        if (ShowUnload)
        {
            var (ux, uy, uw, uh) = UnloadRect();
            canvas.Rect(ux, uy, uw, uh, Inside(mouse, (ux, uy, uw, uh)) ? Palette.ButtonHover : Palette.Button);
            canvas.TextCentered(ux + (uw / 2f), uy + ((uh - (PixelFont.GlyphHeight * layout.Scale)) / 2f), "UNLOAD (U)", layout.Scale, Palette.Text);
        }
    }

    /// <summary>The stance button under <paramref name="point"/>, if the buttons show.</summary>
    public UnitStance? HitStance(Vector2 point)
    {
        if (!ShowStances)
        {
            return null;
        }

        foreach (var stance in Stances)
        {
            if (Inside(point, StanceRect(stance)))
            {
                return stance;
            }
        }

        return null;
    }

    /// <summary>Where a stance button sits: a row just above the footer.</summary>
    public (float X, float Y, float Width, float Height) StanceRect(UnitStance stance)
    {
        var layout = Layout;
        var gap = 4f * layout.Scale;
        var height = 20f * layout.Scale;
        var width = (layout.Width - (2 * layout.Padding) - (2 * gap)) / 3f;
        var footerLines = 2 * (PixelFont.GlyphHeight + 3) * layout.Scale * 1.5f;
        var y = layout.Height - layout.Padding - footerLines - height - (8f * layout.Scale);
        var slot = Array.IndexOf(Stances, stance);
        return (layout.X + layout.Padding + (slot * (width + gap)), y, width, height);
    }

    private static readonly UnitStance[] Stances = [UnitStance.Guard, UnitStance.HoldGround, UnitStance.Free];

    private static string StanceLabel(UnitStance stance) => stance switch
    {
        UnitStance.Guard => "GUARD",
        UnitStance.HoldGround => "HOLD",
        _ => "FREE",
    };

    private static string StanceHelp(UnitStance stance) => stance switch
    {
        UnitStance.Guard => "CHASES, THEN RETURNS",
        UnitStance.HoldGround => "NEVER MOVES TO FIGHT",
        _ => "PURSUES ANYWHERE",
    };

    private void DrawStances(Canvas canvas, SidebarLayout layout, Vector2 mouse)
    {
        if (!ShowStances)
        {
            return;
        }

        foreach (var stance in Stances)
        {
            var (x, y, width, height) = StanceRect(stance);
            var current = CurrentStance == stance;
            var hovered = Inside(mouse, (x, y, width, height));
            canvas.Rect(x, y, width, height, current ? Palette.ButtonActive : hovered ? Palette.ButtonHover : Palette.Button);
            canvas.TextCentered(x + (width / 2f), y + ((height - (PixelFont.GlyphHeight * layout.Scale)) / 2f), StanceLabel(stance), layout.Scale,
                current ? Palette.Text : Palette.TextDim);
            if (current)
            {
                canvas.Frame(x, y, width, height, layout.Scale, Palette.Selection);
            }
        }
    }

    /// <summary>True when a tab not on show has cameos that became available since it was last looked at.</summary>
    public bool NeedsAttention(SidebarTab tab) => _attention[(int)tab];

    /// <summary>Moves to the next (or, with a negative step, previous) tab.</summary>
    public void CycleTab(int step) => ActiveTab = Tabs[(((int)_activeTab + step) % Tabs.Length + Tabs.Length) % Tabs.Length];

    /// <summary>Refreshes every button's state from the simulation.</summary>
    public void Update(GameSimulation simulation, int player, Vector2 framebuffer, int placingStructure)
    {
        Layout = SidebarLayout.For(framebuffer);
        if (player < 0 || player >= simulation.Players.Count)
        {
            return;
        }

        var state = simulation.Players[player];
        var content = simulation.Content;
        if (_faction != state.Faction)
        {
            BuildEntries(content, state.Faction);
        }

        var world = simulation.World;
        var hasYard = simulation.HasCompletedRole(player, StructureRole.ConstructionYard);
        var site = world.IsAlive(state.ConstructionSite) ? world.Get<StructureInfo>(state.ConstructionSite) : (StructureInfo?)null;

        foreach (var entry in _entries)
        {
            entry.Progress = null;
            entry.Queued = 0;
            entry.Reason = null;
            entry.Affordable = state.CanAfford(entry.Cost);
            entry.Placing = entry.IsStructure && entry.TypeIndex == placingStructure;

            if (entry.IsStructure)
            {
                var definition = content.Structure(entry.TypeIndex);
                var missing = definition.Prerequisites.Where(role => !simulation.HasCompletedRole(player, role)).Select(RoleName).FirstOrDefault();
                if (!hasYard)
                {
                    entry.Reason = "NEEDS CONSTRUCTION YARD";
                }
                else if (missing is not null)
                {
                    entry.Reason = "NEEDS " + missing;
                }
                else if (site is { } building && definition.Role != StructureRole.Wall)
                {
                    entry.Reason = "BUILDING";
                    if (building.StructureType == entry.TypeIndex)
                    {
                        entry.Progress = 1f - (building.BuildRemaining / (float)definition.BuildTicks);
                    }
                }

                entry.Available = entry.Reason is null;
            }
            else
            {
                var definition = content.Unit(entry.TypeIndex);
                var producers = 0;
                foreach (var index in world.Store<Producer>().EntityIndices)
                {
                    var info = world.Store<StructureInfo>().Get(index);
                    if (world.Store<Owner>().Get(index).PlayerId != player || !info.IsComplete
                        || content.Structure(info.StructureType).Role != definition.ProducedBy)
                    {
                        continue;
                    }

                    producers++;
                    var producer = world.Store<Producer>().Get(index);
                    entry.Queued += producer.CountOf(entry.TypeIndex);
                    if (!producer.IsIdle && producer.Current == entry.TypeIndex)
                    {
                        var progress = 1f - (producer.Remaining / (float)definition.BuildTicks);
                        entry.Progress = Math.Max(entry.Progress ?? 0f, progress);
                    }
                }

                entry.Reason = producers == 0 ? "NEEDS " + RoleName(definition.ProducedBy) : null;
                entry.Available = producers > 0;
            }
        }

        foreach (var tab in Tabs)
        {
            var available = _entries.Count(entry => entry.Tab == tab && entry.Available);
            if (available > _availableLastUpdate[(int)tab] && tab != _activeTab)
            {
                _attention[(int)tab] = true;
            }

            _availableLastUpdate[(int)tab] = available;
        }
    }

    /// <summary>The cameo under <paramref name="point"/> in the tab on show, if any.</summary>
    public SidebarEntry? HitTest(Vector2 point)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Tab == _activeTab && Inside(point, ButtonRect(i)))
            {
                return _entries[i];
            }
        }

        return null;
    }

    /// <summary>The tab under <paramref name="point"/>, if any.</summary>
    public SidebarTab? HitTab(Vector2 point)
    {
        foreach (var tab in Tabs)
        {
            if (Inside(point, TabRect(tab)))
            {
                return tab;
            }
        }

        return null;
    }

    private static bool Inside(Vector2 point, (float X, float Y, float Width, float Height) rect) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    public bool MinimapContains(Vector2 point)
    {
        var (x, y, size) = Layout.Minimap;
        return point.X >= x && point.Y >= y && point.X < x + size && point.Y < y + size;
    }

    public void Draw(Canvas canvas, GameSimulation simulation, int player, Vector2 mouse, string? status)
    {
        var layout = Layout;
        var text = layout.TextScale;
        canvas.Rect(layout.X, 0, layout.Width, layout.Height, Palette.Panel);
        canvas.Rect(layout.X, 0, 2 * layout.Scale, layout.Height, Palette.PanelEdge);
        if (player < 0 || player >= simulation.Players.Count)
        {
            return;
        }

        var state = simulation.Players[player];
        var left = layout.X + layout.Padding;
        var right = layout.X + layout.Width - layout.Padding;

        canvas.Text(left, layout.CreditsY, "CREDITS", text, Palette.TextDim);
        canvas.TextRight(right, layout.CreditsY, "$" + state.Credits.ToString("N0", CultureInfo.InvariantCulture), text, Palette.Credits);

        DrawPower(canvas, state, left, right, layout);

        DrawTabs(canvas, layout, simulation, player);

        var hovered = HitTest(mouse);
        var team = Palette.Player(player);
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Tab == _activeTab)
            {
                DrawCameo(canvas, layout, _entries[i], ButtonRect(i), team, _entries[i] == hovered);
            }
        }

        DrawStances(canvas, layout, mouse);
        DrawRepair(canvas, layout, mouse);

        // Footer: the hovered cameo's full name and cost, and why it is unavailable (or what a hovered stance does);
        // otherwise the screen's message.
        var footerScale = layout.Scale * 1.5f;
        var lineHeight = (PixelFont.GlyphHeight + 3) * footerScale;
        var bottom = layout.Height - layout.Padding - (PixelFont.GlyphHeight * footerScale);
        if (HitRepair(mouse))
        {
            canvas.Text(left, bottom - lineHeight, RepairOn ? "STOP REPAIRING" : "REPAIR", footerScale, Palette.Text);
            canvas.Text(left, bottom, "PAYS AS IT MENDS", footerScale, Palette.TextDim);
        }
        else if (HitStance(mouse) is { } stanceHovered)
        {
            canvas.Text(left, bottom - lineHeight, "STANCE: " + StanceLabel(stanceHovered), footerScale, Palette.Text);
            canvas.Text(left, bottom, StanceHelp(stanceHovered), footerScale, Palette.TextDim);
        }
        else if (hovered is not null)
        {
            var reason = hovered.Available ? null : hovered.Reason;
            var nameY = reason is null ? bottom : bottom - lineHeight;
            canvas.Text(left, nameY, hovered.Name.ToUpperInvariant(), footerScale, Palette.Text);
            canvas.TextRight(right, nameY, "$" + hovered.Cost.ToString(CultureInfo.InvariantCulture), footerScale,
                hovered.Affordable ? Palette.Credits : Palette.TextWarn);
            if (reason is not null)
            {
                canvas.Text(left, bottom, reason, footerScale, Palette.TextWarn);
            }
        }
        else if (!string.IsNullOrEmpty(status))
        {
            canvas.Text(left, bottom, status, footerScale, Palette.TextDim);
        }
    }

    /// <summary>Where cameo <paramref name="index"/> sits in its tab's grid (whether or not its tab is on show).</summary>
    public (float X, float Y, float Width, float Height) ButtonRect(int index)
    {
        var layout = Layout;
        var tab = _entries[index].Tab;
        var slot = 0;
        for (var i = 0; i < index; i++)
        {
            slot += _entries[i].Tab == tab ? 1 : 0;
        }

        var column = slot % SidebarLayout.Columns;
        var row = slot / SidebarLayout.Columns;
        return (
            layout.X + layout.Padding + (column * (layout.ButtonWidth + layout.ButtonGap)),
            layout.ButtonsY + (row * (layout.ButtonHeight + layout.ButtonGap)),
            layout.ButtonWidth,
            layout.ButtonHeight);
    }

    /// <summary>Where a tab's header sits in the strip above the cameos.</summary>
    public (float X, float Y, float Width, float Height) TabRect(SidebarTab tab)
    {
        var layout = Layout;
        var gap = 2f * layout.Scale;
        var width = (layout.Width - (2 * layout.Padding) - ((Tabs.Length - 1) * gap)) / Tabs.Length;
        return (layout.X + layout.Padding + ((int)tab * (width + gap)), layout.TabsY, width, layout.TabHeight);
    }

    private void DrawTabs(Canvas canvas, SidebarLayout layout, GameSimulation simulation, int player)
    {
        var scale = layout.Scale;
        foreach (var tab in Tabs)
        {
            var (x, y, width, height) = TabRect(tab);
            var active = tab == _activeTab;
            canvas.Rect(x, y, width, height, active ? Palette.ButtonHover : Palette.Button);
            if (active)
            {
                canvas.Rect(x, y, width, 2 * scale, Palette.Player(player));
            }

            var any = _entries.Any(entry => entry.Tab == tab && entry.Available);
            var label = TabLabel(tab);
            var labelColor = active ? Palette.Text : any ? Palette.TextDim : Palette.Scale(Palette.TextDim, 0.6f);
            canvas.TextCentered(x + (width / 2f), y + ((height - (PixelFont.GlyphHeight * scale)) / 2f) + scale, label, scale, labelColor);

            // Something building in this tab: a thin bar along the tab's foot, as far along as the furthest item.
            var progress = _entries.Where(entry => entry.Tab == tab && entry.Progress is not null).Select(entry => entry.Progress!.Value).DefaultIfEmpty(-1f).Max();
            if (progress >= 0f)
            {
                canvas.Rect(x, y + height - (2 * scale), width * Math.Clamp(progress, 0f, 1f), 2 * scale, Palette.Progress);
            }

            if (_attention[(int)tab] && !active)
            {
                var pulse = 0.55f + (0.45f * MathF.Sin(simulation.Tick * 0.6f));
                canvas.Disc(new Vector2(x + width - (5 * scale), y + (5 * scale)), 2.5f * scale, 2.5f * scale, Palette.WithAlpha(Palette.TextGood, pulse));
            }
        }
    }

    private static void DrawCameo(Canvas canvas, SidebarLayout layout, SidebarEntry entry, (float X, float Y, float Width, float Height) rect, LinearColor team, bool hovered)
    {
        var (x, y, width, height) = rect;
        var scale = layout.Scale;
        canvas.Rect(x, y, width, height, entry.Placing ? Palette.ButtonActive : hovered && entry.Available ? Palette.ButtonHover : Palette.Button);

        // The icon fills the cameo above the caption strip.
        var caption = (PixelFont.GlyphHeight + 4) * scale;
        CameoIcons.Draw(canvas, entry.Id, x + (3 * scale), y + (3 * scale), width - (6 * scale), height - caption - (3 * scale), team, dim: !entry.Available);

        // Building: the part still to come is shaded, the classic wipe, with a bar along the icon's foot.
        if (entry.Progress is { } progress)
        {
            var done = Math.Clamp(progress, 0f, 1f);
            canvas.Rect(x, y, width, (height - caption) * (1f - done), new LinearColor(0f, 0f, 0f, 0.5f));
            canvas.Rect(x, y + height - caption - (2 * scale), width * done, 2 * scale, Palette.Progress);
        }

        var captionY = y + height - caption + (2 * scale);
        canvas.Rect(x, y + height - caption, width, caption, new LinearColor(0f, 0f, 0f, 0.35f));
        canvas.Text(x + (3 * scale), captionY, entry.Label, scale, entry.Available ? Palette.Text : Palette.TextDim);
        canvas.TextRight(x + width - (3 * scale), captionY, "$" + entry.Cost.ToString(CultureInfo.InvariantCulture), scale,
            !entry.Available ? Palette.TextDim : entry.Affordable ? Palette.Credits : Palette.TextWarn);

        if (entry.Queued > 1)
        {
            var count = entry.Queued.ToString(CultureInfo.InvariantCulture);
            var badge = PixelFont.Measure(count, 2 * scale) + (4 * scale);
            canvas.Rect(x + width - badge - (2 * scale), y + (2 * scale), badge, (PixelFont.GlyphHeight + 4) * scale, new LinearColor(0f, 0f, 0f, 0.6f));
            canvas.Text(x + width - badge, y + (4 * scale), count, 2 * scale, Palette.Credits);
        }

        if (entry.Placing)
        {
            canvas.Frame(x, y, width, height, 2 * scale, Palette.Selection);
        }
        else if (hovered)
        {
            canvas.Frame(x, y, width, height, scale, Palette.PanelEdge);
        }
    }

    private static string TabLabel(SidebarTab tab) => tab switch
    {
        SidebarTab.Base => "BASE",
        SidebarTab.Defence => "DEFENCE",
        SidebarTab.Infantry => "INFANTRY",
        _ => "VEHICLES",
    };

    private static void DrawPower(Canvas canvas, PlayerState state, float left, float right, SidebarLayout layout)
    {
        var y = layout.PowerY;
        var barHeight = 10f * layout.Scale;
        var width = right - left;
        var low = state.HasLowPower;
        var max = Math.Max(1, Math.Max(state.PowerSupply, state.PowerDemand));
        canvas.Rect(left, y, width, barHeight, Palette.PanelLight);
        canvas.Rect(left, y, width * (state.PowerSupply / (float)max), barHeight, low ? Palette.TextWarn : Palette.TextGood);
        var demandX = left + (width * (state.PowerDemand / (float)max));
        canvas.Rect(demandX - layout.Scale, y - (2 * layout.Scale), 2 * layout.Scale, barHeight + (4 * layout.Scale), Palette.Text);
        var label = low ? "LOW POWER" : "POWER";
        canvas.Text(left, y + barHeight + (2 * layout.Scale), label, layout.Scale, low ? Palette.TextWarn : Palette.TextDim);
        canvas.TextRight(right, y + barHeight + (2 * layout.Scale),
            state.PowerDemand.ToString(CultureInfo.InvariantCulture) + "/" + state.PowerSupply.ToString(CultureInfo.InvariantCulture),
            layout.Scale, Palette.TextDim);
    }

    private void BuildEntries(GameContent content, int faction)
    {
        _faction = faction;
        _entries.Clear();
        var definition = content.Faction(faction);
        foreach (var id in definition.StructureIds)
        {
            var index = content.StructureIndex(id);
            var structure = content.Structure(index);
            if (structure.Role != StructureRole.ConstructionYard)
            {
                var tab = structure.Role is StructureRole.Wall or StructureRole.Defence ? SidebarTab.Defence : SidebarTab.Base;
                _entries.Add(new SidebarEntry(true, index, id, ShortName(structure.Name), structure.Name, structure.Cost, tab));
            }
        }

        foreach (var id in definition.UnitIds)
        {
            var index = content.UnitIndex(id);
            var unit = content.Unit(index);
            var tab = unit.ProducedBy == StructureRole.Barracks ? SidebarTab.Infantry : SidebarTab.Vehicles;
            _entries.Add(new SidebarEntry(false, index, id, ShortName(unit.Name), unit.Name, unit.Cost, tab));
        }
    }

    // Button labels fit about eleven characters beside the price.
    private static string ShortName(string name)
    {
        var upper = name.ToUpperInvariant();
        return upper.Length <= 11 ? upper : upper.Split(' ')[0];
    }

    private static string RoleName(StructureRole role) => role switch
    {
        StructureRole.ConstructionYard => "CONSTRUCTION YARD",
        StructureRole.PowerPlant => "POWER PLANT",
        StructureRole.Refinery => "REFINERY",
        StructureRole.Barracks => "BARRACKS",
        StructureRole.Factory => "WAR FACTORY",
        _ => role.ToString().ToUpperInvariant(),
    };
}
