using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.UI.Menu;
using Brutal.GlfwApi;

namespace Ashfall.Client;

/// <summary>
/// The title-screen loop: show the main menu, run whatever the player picks (skirmish, host, join),
/// then come back to the menu, until they quit or close the window.
/// </summary>
internal static class MenuClient
{
    public static async Task<int> Run(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options)
    {
        var settingsPath = MenuSettings.DefaultPath;
        var settings = MenuSettings.Load(settingsPath);
        if (options.NameGiven)
        {
            settings = settings with { Name = options.Name };
        }

        string? message = null;
        while (!window.ShouldClose && !FramesExhausted(options, renderer))
        {
            var choice = ShowMenu(window, renderer, input, options, settings, message);
            message = null;
            if (choice is null || choice.Action == MenuAction.Quit)
            {
                break;
            }

            settings = choice.Settings;
            settings.Save(settingsPath);
            window.Title = "Ashfall";
            switch (choice.Action)
            {
                case MenuAction.Skirmish:
                    await SkirmishClient.Run(window, renderer, input, options with
                    {
                        Seed = settings.Seed,
                        Opponent = settings.IdleOpponent ? PlayerController.IdleAi : PlayerController.ScriptedAi,
                    }).ConfigureAwait(false);
                    break;

                case MenuAction.Host:
                    message = MultiplayerClient.Run(window, renderer, input, options with
                    {
                        Host = true,
                        Humans = settings.Humans,
                        Ais = settings.Ais,
                        Port = settings.Port,
                        Name = settings.Name,
                        Seed = options.SeedGiven ? options.Seed : (ulong)Random.Shared.NextInt64(1, 1_000_000_000),
                    }).Message;
                    break;

                case MenuAction.Join:
                    var address = Resolve(choice.JoinHost, choice.JoinPort, out message);
                    if (address is not null)
                    {
                        message = MultiplayerClient.Run(window, renderer, input, options with
                        {
                            Join = address.ToString(),
                            Name = settings.Name,
                        }).Message;
                    }

                    break;
            }
        }

        return 0;
    }

    private static MenuChoice? ShowMenu(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options, MenuSettings settings, string? message)
    {
        var menu = new MainMenuScreen(settings, message);
        var scene = new FrameScene();
        var clock = Stopwatch.StartNew();
        input.State.EndFrame();
        window.Title = "Ashfall";
        while (!window.ShouldClose && menu.Choice is null)
        {
            if (FramesExhausted(options, renderer))
            {
                break;
            }

            Glfw.PollEvents();
            input.Poll();
            input.State.Time = clock.Elapsed.TotalSeconds;
            menu.Update(input.State, new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y));
            menu.Draw(scene);
            if (options.Screenshot is not null && options.Frames > 0 && renderer.FramesPresented == options.Frames - 1)
            {
                renderer.RequestCapture();
            }

            if (renderer.RenderFrame(scene) == FrameResult.Skipped)
            {
                Thread.Sleep(5);
            }

            input.State.EndFrame();
        }

        if (options.Screenshot is not null && menu.Choice is null && renderer.LastCapture is { } capture)
        {
            PngEncoder.WriteFile(options.Screenshot, capture);
            Console.WriteLine($"Saved {capture.Size} screenshot to {options.Screenshot}.");
        }

        return menu.Choice;
    }

    /// <summary>Turns what the player typed into an address: IPv4 literals directly, names through DNS.</summary>
    private static IPEndPoint? Resolve(string host, int port, out string? error)
    {
        error = null;
        if (IPAddress.TryParse(host, out var literal))
        {
            return new IPEndPoint(literal, port);
        }

        try
        {
            var address = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (address is not null)
            {
                return new IPEndPoint(address, port);
            }

            error = $"{host} has no IPv4 address.";
        }
        catch (SocketException e)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"Could not look up {host}: {e.Message}");
        }
        catch (ArgumentException)
        {
            error = $"{host} is not a valid host name.";
        }

        return null;
    }

    private static bool FramesExhausted(ClientOptions options, VulkanRenderer renderer) =>
        options.Frames > 0 && renderer.FramesPresented >= options.Frames;
}
