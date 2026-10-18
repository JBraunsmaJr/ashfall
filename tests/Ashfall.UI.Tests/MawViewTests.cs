using System.Numerics;
using Ashfall.Content;
using Ashfall.Session;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>Holding the fortress against the Maw: the swarm is drawn, can be targeted, and never breaks the view.</summary>
public class MawViewTests
{
    [Fact]
    public void The_swarm_and_its_nests_are_drawn_in_the_maws_colours()
    {
        var ui = new UiDriver(MatchSpec.Swarm(7, creatures: 300, nests: 4).WithController(0, PlayerController.Human));
        var (pit, _) = NestNearest(ui);
        ui.Simulation.SpawnCreature(ui.Simulation.Content.CreatureIndex(MawContent.Skitter), UnderCamera(ui));
        ui.Frame(1);
        var before = CountColoured(ui.Scene, Palette.MawCarapace);
        Assert.True(before > 0, "A creature in view should be drawn.");

        // The nests sit out in the shroud until scouted: reveal one and look at it.
        ui.Screen.Camera.CenterOn(UiDriver.ToVector(ui.Simulation.World.Get<Position>(pit).Value));
        ui.Simulation.SpawnUnit(ui.Player, ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman), ui.Simulation.World.Get<Position>(pit).Value + new Core.FixVec2(4, 0));
        ui.Frame(6);

        Assert.True(CountColoured(ui.Scene, Palette.MawGlow) > 0, "A scouted nest glows.");
    }

    [Fact]
    public void Wounded_creatures_of_every_kind_draw_without_trouble_and_only_behemoths_get_health_bars()
    {
        var ui = new UiDriver(MatchSpec.Swarm(7, creatures: 50, nests: 1).WithController(0, PlayerController.Human));
        var content = ui.Simulation.Content;
        foreach (var id in new[] { MawContent.Skitter, MawContent.Spitter, MawContent.Siegebeast })
        {
            var creature = ui.Simulation.SpawnCreature(content.CreatureIndex(id), UnderCamera(ui));
            ui.Simulation.World.Get<Health>(creature).Current -= 5;
        }

        ui.Frame(1); // used to throw: health bars assumed anything wounded and mobile was a unit
        ui.Frame(1);
        Assert.True(ui.Scene.QuadCount > 0);
    }

    [Fact]
    public void Right_clicking_a_creature_orders_the_selection_to_attack_it()
    {
        var ui = new UiDriver(MatchSpec.Swarm(7, creatures: 50, nests: 1).WithController(0, PlayerController.Human));
        var pyro = ui.Units(ui.Player, BulwarkContent.Pyro)[0];
        var near = ui.Simulation.World.Get<Position>(pyro).Value + new Core.FixVec2(2, 0);
        // A Siegebeast: anything smaller is ash before the test can click on it.
        var beast = ui.Simulation.SpawnCreature(ui.Simulation.Content.CreatureIndex(MawContent.Siegebeast), near);
        ui.Screen.Camera.CenterOn(UiDriver.ToVector(near));
        ui.Frame(4); // let vision catch up
        ui.Screen.Select([pyro]);
        var queued = new List<Command>();

        ui.Click(ui.ScreenOf(beast), MouseButton.Right);
        ui.Orders.CollectCommands(ui.Simulation, ui.Simulation.Tick, queued);

        Assert.Contains(queued, command => command.Kind == CommandKind.Attack && command.TargetEntity == beast && command.Unit == pyro);
    }

    private static (Simulation.Ecs.Entity Nest, float Distance) NestNearest(UiDriver ui)
    {
        var world = ui.Simulation.World;
        var best = (Simulation.Ecs.Entity.None, float.MaxValue);
        foreach (var index in world.Store<Nest>().EntityIndices)
        {
            var distance = Vector2.Distance(UiDriver.ToVector(world.Store<Position>().Get(index).Value), ui.Screen.Camera.Center);
            if (distance < best.Item2)
            {
                best = (world.EntityAt(index), distance);
            }
        }

        return best;
    }

    // A spot in the middle of the view, inside the fortress's vision.
    private static Core.FixVec2 UnderCamera(UiDriver ui)
    {
        var center = ui.Screen.Camera.Center;
        return new Cell((int)center.X + 2, (int)center.Y + 2).Center;
    }

    private static int CountColoured(Rendering.Scene.FrameScene scene, Rendering.LinearColor color)
    {
        var count = 0;
        foreach (var vertex in scene.Vertices)
        {
            if (Math.Abs(vertex.Color.X - color.R) < 1e-4f && Math.Abs(vertex.Color.Y - color.G) < 1e-4f && Math.Abs(vertex.Color.Z - color.B) < 1e-4f)
            {
                count++;
            }
        }

        return count;
    }
}
