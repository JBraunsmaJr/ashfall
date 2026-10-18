using System.Numerics;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Input;
using Ashfall.MapGen;
using Ashfall.UI.Menu;

namespace Ashfall.UI.Tests;

public class MainMenuTests
{
    private static readonly Vector2 Framebuffer = new(1280, 720);
    private static readonly string[] BothRecentHosts = ["10.0.0.5", "example.net:48000"];

    [Fact]
    public void Joining_by_typed_address_produces_a_join_choice_and_remembers_the_host()
    {
        var menu = new MenuDriver(new MenuSettings { Name = "JON" });
        menu.Click("JOIN GAME");
        Assert.Equal("Join", menu.Screen.CurrentPage);

        // The address box has focus on arrival; letters that cannot be in an address are dropped.
        menu.Type("192.168.1.20:476012 !");
        menu.Press(Key.Backspace);
        menu.Click("JOIN");

        var choice = Assert.IsType<MenuChoice>(menu.Screen.Choice);
        Assert.Equal(MenuAction.Join, choice.Action);
        Assert.Equal("192.168.1.20", choice.JoinHost);
        Assert.Equal(47601, choice.JoinPort);
        Assert.Equal("JON", choice.Settings.Name);
        Assert.Equal("192.168.1.20:47601", choice.Settings.RecentHosts[0]);
    }

    [Fact]
    public void Join_stays_disabled_until_the_address_parses()
    {
        var menu = new MenuDriver(new MenuSettings());
        menu.Click("JOIN GAME");
        Assert.False(menu.Enabled("JOIN"));
        menu.Type("host:99999");
        Assert.False(menu.Enabled("JOIN"));
        for (var i = 0; i < 6; i++)
        {
            menu.Press(Key.Backspace); // down to "host"
        }

        Assert.True(menu.Enabled("JOIN"));
        menu.Press(Key.Enter);
        Assert.Equal("host", menu.Screen.Choice?.JoinHost);
        Assert.Equal(HostAddress.DefaultPort, menu.Screen.Choice?.JoinPort);
    }

    [Fact]
    public void A_recent_host_fills_the_address_box_when_clicked()
    {
        var menu = new MenuDriver(new MenuSettings().WithRecentHost("10.0.0.5").WithRecentHost("example.net:48000"));
        menu.Click("JOIN GAME");
        menu.Click("10.0.0.5");
        menu.Click("JOIN");
        Assert.Equal("10.0.0.5", menu.Screen.Choice?.JoinHost);
        Assert.Equal(BothRecentHosts, menu.Screen.Choice!.Settings.RecentHosts);
    }

    [Fact]
    public void Hosting_keeps_two_to_four_players_and_carries_name_and_port()
    {
        var menu = new MenuDriver(new MenuSettings { Name = "JON", Humans = 2, Ais = 0 });
        menu.Click("HOST GAME");

        // PLAYERS and AI each have a - and a + (in that order).
        Assert.False(menu.Enabled("-", 0)); // two players is the minimum
        menu.Click("+", 1);
        menu.Click("+", 1);
        Assert.False(menu.Enabled("+", 0)); // four in total is the maximum
        Assert.False(menu.Enabled("+", 1));

        menu.Click("NAME");
        menu.Press(Key.Backspace);
        menu.Type("N");
        menu.Click("PORT");
        for (var i = 0; i < 5; i++)
        {
            menu.Press(Key.Backspace);
        }

        Assert.False(menu.Enabled("HOST"));
        menu.Type("50000x");
        menu.Click("HOST");

        var choice = Assert.IsType<MenuChoice>(menu.Screen.Choice);
        Assert.Equal(MenuAction.Host, choice.Action);
        Assert.Equal(("JON", 2, 2, 50000), (choice.Settings.Name, choice.Settings.Humans, choice.Settings.Ais, choice.Settings.Port));
    }

    [Fact]
    public void Tab_moves_between_boxes_and_Esc_goes_back_to_the_main_page()
    {
        var menu = new MenuDriver(new MenuSettings { Name = "A" });
        menu.Click("JOIN GAME");
        menu.Press(Key.Tab); // the address box has focus; Tab wraps round to the name box
        menu.Type("B");
        Assert.Equal("AB", menu.Screen.Settings.Name);
        menu.Press(Key.Escape);
        Assert.Equal("Main", menu.Screen.CurrentPage);
        Assert.Null(menu.Screen.Choice);
    }

    [Fact]
    public void Skirmish_carries_the_opponent_and_seed_and_quit_quits()
    {
        var menu = new MenuDriver(new MenuSettings { Seed = 5 });
        menu.Click("SKIRMISH");
        menu.Click("IDLE");
        menu.Click("SEED");
        menu.Type("7");
        menu.Click("START");
        var choice = Assert.IsType<MenuChoice>(menu.Screen.Choice);
        Assert.Equal(MenuAction.Skirmish, choice.Action);
        Assert.True(choice.Settings.IdleOpponent);
        Assert.Equal(57UL, choice.Settings.Seed);

        var quit = new MenuDriver(new MenuSettings());
        quit.Click("QUIT");
        Assert.Equal(MenuAction.Quit, quit.Screen.Choice?.Action);
    }

    [Fact]
    public void The_map_is_picked_with_archetype_buttons_slider_bars_and_pasted_codes()
    {
        var menu = new MenuDriver(new MenuSettings { Map = null });
        menu.Click("SKIRMISH");
        Assert.Null(menu.Screen.Map);
        Assert.Equal("Duel", menu.Screen.Preview.Map?.Name);

        menu.Click("RIVERLANDS");
        Assert.Equal(MapArchetypes.Riverlands, menu.Screen.Map?.Archetype);
        Assert.Equal(MapArchetypes.Riverlands, menu.Screen.Preview.Map?.Generated?.Requested.Archetype);

        // Clicking the middle of a bar sets its slider to about half; the preview follows.
        menu.ClickBar("ELEVATION", 0.5f);
        Assert.InRange(menu.Screen.Map![MapSlider.Elevation], 48, 52);
        menu.ClickBar("SIZE", 1f);
        Assert.Equal(100, menu.Screen.Map![MapSlider.Size]);
        Assert.Equal(128, menu.Screen.Preview.Map?.Grid.Width);

        // Pasting a map code into the code box switches to exactly that map.
        var shared = new MapSettings(MapArchetypes.Crossroads, 777).With(MapSlider.Water, 90);
        menu.Click("CODE");
        for (var i = 0; i < 30; i++)
        {
            menu.Press(Key.Backspace);
        }

        menu.Type(shared.ToCode());
        Assert.Equal(shared, menu.Screen.Map);

        menu.Click("START");
        Assert.Equal(shared, menu.Screen.Choice?.Settings.Map);
        Assert.Equal(shared, MenuSettings.Parse(menu.Screen.Choice!.Settings.Serialize()).Map);
    }

    [Fact]
    public void Hosting_a_hand_made_map_and_remembering_it()
    {
        var menu = new MenuDriver(new MenuSettings());
        menu.Click("HOST GAME");
        menu.Click("HAND-MADE");
        menu.Click("+", 1); // an AI: three players, so the preview shows the quad map
        Assert.Equal("Quad", menu.Screen.Preview.Map?.Name);
        menu.Click("HOST");
        Assert.Null(menu.Screen.Choice?.Settings.Map);
        Assert.Null(MenuSettings.Parse(menu.Screen.Choice!.Settings.Serialize()).Map);
    }

    [Fact]
    public void A_failure_message_shows_on_the_main_page_until_the_player_moves_on()
    {
        var menu = new MenuDriver(new MenuSettings(), "Could not reach the host.");
        Assert.Equal("Could not reach the host.", menu.Screen.Message);
        Assert.True(menu.Scene.QuadCount > 200);
        menu.Click("JOIN GAME");
        Assert.Null(menu.Screen.Message);
    }

    [Theory]
    [InlineData("203.0.113.7", true, "203.0.113.7", 47600)]
    [InlineData(" 203.0.113.7:1 ", true, "203.0.113.7", 1)]
    [InlineData("my-host.example.net:47610", true, "my-host.example.net", 47610)]
    [InlineData("", false, "", 47600)]
    [InlineData(":47600", false, "", 47600)]
    [InlineData("host:0", false, "", 0)]
    [InlineData("host:65536", false, "", 65536)]
    [InlineData("a:b:c", false, "", 47600)]
    public void Host_addresses_parse_with_an_optional_port(string text, bool ok, string host, int port)
    {
        Assert.Equal(ok, HostAddress.TryParse(text, out var parsedHost, out var parsedPort));
        if (ok)
        {
            Assert.Equal((host, port), (parsedHost, parsedPort));
        }
    }

    [Fact]
    public void Settings_round_trip_and_tolerate_junk()
    {
        var settings = new MenuSettings { Name = "ANA", Seed = 99, IdleOpponent = true, Humans = 3, Ais = 1, Port = 47700 }
            .WithRecentHost("a.example").WithRecentHost("10.0.0.1:5000");
        Assert.Equal(settings.Serialize(), MenuSettings.Parse(settings.Serialize()).Serialize());

        var junk = MenuSettings.Parse("﻿name=\nseed=-4\nhumans=9\nport=0\nrecent=:::\nwhat=ever\nnot a line\nais=3\n");
        Assert.Equal("PLAYER", junk.Name);
        Assert.Equal(new MenuSettings().Seed, junk.Seed);
        Assert.Equal((2, 0), (junk.Humans, junk.Ais)); // 2 humans + 3 AIs is too many: back to the default 1v1
        Assert.Equal(HostAddress.DefaultPort, junk.Port);
        Assert.Empty(junk.RecentHosts);
    }

    [Fact]
    public void Settings_survive_a_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), "ashfall-menu-" + Guid.NewGuid().ToString("N"), "menu.cfg");
        try
        {
            Assert.Equal(new MenuSettings().Serialize(), MenuSettings.Load(path).Serialize());
            var settings = new MenuSettings { Name = "KAI" }.WithRecentHost("1.2.3.4");
            settings.Save(path);
            Assert.Equal(settings.Serialize(), MenuSettings.Load(path).Serialize());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Text_fields_only_hold_what_they_accept()
    {
        var field = new TextField("PORT", 5, char.IsAsciiDigit) { Text = "12a34567" };
        Assert.Equal("12345", field.Text);
        var input = new InputState();
        input.Type("9");
        Assert.False(field.Edit(input)); // not focused
        field.Focused = true;
        input.SetKey(Key.Backspace, true);
        Assert.True(field.Edit(input));
        Assert.Equal("12349", field.Text);
    }

    [Fact]
    public void Esc_twice_with_nothing_to_cancel_leaves_a_match_in_progress()
    {
        var ui = new UiDriver();
        ui.Press(Key.Escape);
        Assert.False(ui.Screen.ExitRequested);
        ui.Press(Key.Escape);
        Assert.True(ui.Screen.ExitRequested);

        // Waiting too long between the two presses starts over.
        var slow = new UiDriver();
        slow.Press(Key.Escape);
        for (var i = 0; i < 60 * 4; i++)
        {
            slow.Frame();
        }

        slow.Press(Key.Escape);
        Assert.False(slow.Screen.ExitRequested);
    }

    /// <summary>Drives the menu with clicks and typing, one frame at a time.</summary>
    private sealed class MenuDriver
    {
        private readonly InputState _input = new() { Mouse = new Vector2(5, 5) };
        private double _time;

        public MenuDriver(MenuSettings settings, string? message = null)
        {
            Screen = new MainMenuScreen(settings, message) { Preview = new MapPreview { Synchronous = true } };
            Frame();
        }

        public MainMenuScreen Screen { get; }

        public FrameScene Scene { get; } = new();

        public void Frame()
        {
            _time += 1 / 60.0;
            _input.Time = _time;
            Screen.Update(_input, Framebuffer);
            Screen.Draw(Scene);
            _input.EndFrame();
        }

        public bool Enabled(string label, int index = 0) => Target(label, index).Enabled;

        public void Click(string label, int index = 0)
        {
            var target = Target(label, index);
            Assert.True(target.Enabled, label + " is disabled");
            _input.Mouse = target.Center;
            _input.SetButton(MouseButton.Left, true);
            Frame();
            _input.SetButton(MouseButton.Left, false);
            Frame();
        }

        public void ClickBar(string label, float fraction)
        {
            var target = Target(label, 0);
            var left = target.Center.X - (target.Size.X / 2);
            _input.Mouse = new Vector2(left + (Math.Min(fraction, 0.999f) * target.Size.X), target.Center.Y);
            _input.SetButton(MouseButton.Left, true);
            Frame();
            _input.SetButton(MouseButton.Left, false);
            Frame();
        }

        public void Type(string text)
        {
            _input.Type(text);
            Frame();
        }

        public void Press(Key key)
        {
            _input.SetKey(key, true);
            Frame();
            _input.SetKey(key, false);
            Frame();
        }

        private (string Label, Vector2 Center, Vector2 Size, bool Enabled) Target(string label, int index)
        {
            var matches = Screen.Targets.Where(t => t.Label == label).ToList();
            Assert.True(matches.Count > index, $"No '{label}' #{index} on the {Screen.CurrentPage} page: {string.Join(", ", Screen.Targets.Select(t => t.Label))}");
            return matches[index];
        }
    }
}
