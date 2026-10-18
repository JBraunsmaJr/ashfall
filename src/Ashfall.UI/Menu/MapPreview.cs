using Ashfall.MapGen;
using Ashfall.Rendering;
using Ashfall.Session.Maps;
using Ashfall.Simulation.Map;
using Ashfall.UI.Drawing;
using Ashfall.UI.Text;
using Ashfall.UI.World;

namespace Ashfall.UI.Menu;

/// <summary>
/// A live minimap of the map the current settings make. Generation runs on a worker thread so dragging
/// a slider never stalls the frame; the newest request wins and older results are dropped. The
/// generator is pure, so this is the same map every peer will build.
/// </summary>
public sealed class MapPreview
{
    private readonly object _gate = new();
    private (MapSettings? Settings, int Players) _wanted = (null, 2);
    private (MapSettings? Settings, int Players)? _shown;
    private MatchMap? _map;
    private int _version;
    private bool _busy;

    /// <summary>Generate on the calling thread (tests and screenshots) instead of in the background.</summary>
    public bool Synchronous { get; init; }

    /// <summary>The newest finished map, or null while the first one is generating.</summary>
    public MatchMap? Map
    {
        get
        {
            lock (_gate)
            {
                return _map;
            }
        }
    }

    /// <summary>Whether the shown map is out of date (a newer one is generating).</summary>
    public bool Busy
    {
        get
        {
            lock (_gate)
            {
                return _busy;
            }
        }
    }

    /// <summary>Asks for the map these settings make (null: the hand-made map) for <paramref name="players"/> players.</summary>
    public void Request(MapSettings? settings, int players)
    {
        int version;
        lock (_gate)
        {
            var wanted = (settings, players);
            if (_wanted == wanted && (_shown == wanted || _busy))
            {
                return;
            }

            _wanted = wanted;
            version = ++_version;
            _busy = true;
        }

        if (Synchronous)
        {
            Complete(version, MatchMap.Create(settings, players), (settings, players));
            return;
        }

        _ = Task.Run(() => Complete(version, MatchMap.Create(settings, players), (settings, players)));
    }

    private void Complete(int version, MatchMap map, (MapSettings?, int) request)
    {
        lock (_gate)
        {
            if (version != _version)
            {
                return; // a newer request is on its way
            }

            _map = map;
            _shown = request;
            _busy = false;
        }
    }

    /// <summary>Draws the preview in a square: terrain, fields and numbered start positions.</summary>
    public void Draw(Canvas canvas, float x, float y, float size, float textScale)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.Rect(x, y, size, size, Palette.Clear);
        var map = Map;
        if (map is null)
        {
            canvas.TextCentered(x + (size / 2), y + (size / 2) - (4 * textScale), "GENERATING...", textScale, Palette.TextDim);
            return;
        }

        var grid = map.Grid;
        var cell = size / grid.Width;
        for (var row = 0; row < grid.Height; row++)
        {
            // Runs of equal colour, so a 128-cell map is a few thousand rects rather than sixteen thousand.
            var runStart = 0;
            var runColor = TerrainColors.Of(grid, new Cell(0, row));
            for (var column = 1; column <= grid.Width; column++)
            {
                var color = column < grid.Width ? TerrainColors.Of(grid, new Cell(column, row)) : default;
                if (column < grid.Width && color == runColor)
                {
                    continue;
                }

                canvas.Rect(x + (runStart * cell), y + (row * cell), (column - runStart) * cell + 0.5f, cell + 0.5f, runColor);
                runStart = column;
                runColor = color;
            }
        }

        for (var player = 0; player < map.StartOrigins.Count; player++)
        {
            var origin = map.StartOrigins[player];
            var px = x + ((origin.X - 1) * cell);
            var py = y + ((origin.Y - 1) * cell);
            canvas.Rect(px, py, 5 * cell, 5 * cell, Palette.Player(player));
            canvas.TextCentered(px + (2.5f * cell), py + (2.5f * cell) - (PixelFont.GlyphHeight * textScale / 2), (player + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), textScale, Palette.Text);
        }

        if (Busy)
        {
            canvas.Rect(x, y, size, size, Palette.WithAlpha(Palette.Clear, 0.35f));
        }

        canvas.Frame(x, y, size, size, 1, Palette.PanelEdge);
    }
}
