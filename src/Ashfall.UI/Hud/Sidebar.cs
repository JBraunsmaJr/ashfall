using System.Globalization;
using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Players;
using Ashfall.UI.Drawing;
using Ashfall.UI.Text;

namespace Ashfall.UI.Hud;

/// <summary>
/// The classic right-hand sidebar (GDD section 5): credits, power bar, radar, then one button per
/// structure and unit the faction can build. Buttons show cost, progress and queue length, and grey
/// out with a reason when prerequisites are missing.
/// </summary>
public sealed class Sidebar
{
    private readonly List<SidebarEntry> _entries = [];
    private int _faction = -1;

    public IReadOnlyList<SidebarEntry> Entries => _entries;

    public SidebarLayout Layout { get; private set; }

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
    }

    /// <summary>The button under <paramref name="point"/>, if any.</summary>
    public SidebarEntry? HitTest(Vector2 point)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var (x, y, width, height) = ButtonRect(i);
            if (point.X >= x && point.X < x + width && point.Y >= y && point.Y < y + height)
            {
                return _entries[i];
            }
        }

        return null;
    }

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

        // Buttons.
        var hovered = HitTest(mouse);
        var lastWasStructure = true;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var (x, y, width, height) = ButtonRect(i);
            if (i == 0 || entry.IsStructure != lastWasStructure)
            {
                canvas.Text(x, y - layout.HeaderHeight + (3 * layout.Scale), entry.IsStructure ? "STRUCTURES" : "UNITS", layout.Scale, Palette.TextDim);
            }

            lastWasStructure = entry.IsStructure;
            var background = entry.Placing ? Palette.ButtonActive : entry == hovered && entry.Available ? Palette.ButtonHover : Palette.Button;
            canvas.Rect(x, y, width, height, background);
            if (entry.Progress is { } progress)
            {
                canvas.Rect(x, y, width * Math.Clamp(progress, 0f, 1f), height, Palette.Progress);
            }

            var labelColor = entry.Available ? Palette.Text : Palette.TextDim;
            var labelY = y + ((height - (PixelFont.GlyphHeight * text)) / 2f);
            var label = entry.Queued > 1 ? entry.Label + " " + "×" + entry.Queued.ToString(CultureInfo.InvariantCulture) : entry.Label;
            canvas.Text(x + (5 * layout.Scale), labelY, label, text, labelColor);
            canvas.TextRight(x + width - (5 * layout.Scale), labelY, "$" + entry.Cost.ToString(CultureInfo.InvariantCulture), text,
                !entry.Available ? Palette.TextDim : entry.Affordable ? Palette.Credits : Palette.TextWarn);
        }

        // Status line: the hovered button's reason, or the screen's own message.
        var footer = hovered is { Available: false, Reason: { } reason } ? reason : status;
        if (!string.IsNullOrEmpty(footer))
        {
            canvas.Text(left, layout.Height - layout.Padding - (PixelFont.GlyphHeight * layout.Scale * 1.5f), footer, layout.Scale * 1.5f, Palette.TextDim);
        }
    }

    public (float X, float Y, float Width, float Height) ButtonRect(int index)
    {
        var layout = Layout;
        var structures = _entries.Count(entry => entry.IsStructure);
        var group = index < structures ? 0 : 1;
        var y = layout.ButtonsY + layout.HeaderHeight + (index * (layout.ButtonHeight + layout.ButtonGap)) + (group * (layout.HeaderHeight + (4 * layout.Scale)));
        return (layout.X + layout.Padding, y, layout.Width - (2 * layout.Padding), layout.ButtonHeight);
    }

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
                _entries.Add(new SidebarEntry(true, index, ShortName(structure.Name), structure.Cost));
            }
        }

        foreach (var id in definition.UnitIds)
        {
            var index = content.UnitIndex(id);
            var unit = content.Unit(index);
            _entries.Add(new SidebarEntry(false, index, ShortName(unit.Name), unit.Cost));
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
