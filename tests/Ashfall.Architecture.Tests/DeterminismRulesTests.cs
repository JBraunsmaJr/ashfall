using System.Reflection;
using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Architecture.Tests;

/// <summary>
/// Backstop for the banned-API analyzer: no floating-point type may appear anywhere in the shape of
/// the deterministic assemblies (fields, properties, parameters, return types, generic arguments).
/// </summary>
public class DeterminismRulesTests
{
    private static readonly Type[] FloatingPointTypes = [typeof(float), typeof(double), typeof(decimal), typeof(Half)];

    private const BindingFlags EveryMember =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TheoryData<string> DeterministicAssemblies => new() { "Ashfall.Core", "Ashfall.Content", "Ashfall.Simulation", "Ashfall.MapGen" };

    [Theory]
    [MemberData(nameof(DeterministicAssemblies))]
    public void No_floating_point_types_in_deterministic_assemblies(string assemblyName)
    {
        var assembly = assemblyName switch
        {
            "Ashfall.Core" => typeof(Fix64).Assembly,
            "Ashfall.Content" => typeof(GameContent).Assembly,
            "Ashfall.MapGen" => typeof(Ashfall.MapGen.MapGenerator).Assembly,
            _ => typeof(GameSimulation).Assembly,
        };

        var violations = assembly.GetTypes().SelectMany(FloatingPointUsages);

        Assert.Empty(violations);
    }

    [Fact]
    public void Every_registered_component_is_unmanaged_and_hashable()
    {
        var componentTypes = typeof(GameSimulation).Assembly.GetTypes()
            .Where(type => type.Namespace == "Ashfall.Simulation.Components" && type.IsValueType)
            .ToList();

        Assert.NotEmpty(componentTypes);
        Assert.DoesNotContain(componentTypes, type => type.IsEnum); // enums belong with their concept (Orders, Map, ...)
        foreach (var type in componentTypes)
        {
            Assert.True(typeof(IStateHashable).IsAssignableFrom(type), $"{type.Name} must implement IStateHashable.");
            Assert.True(IsUnmanaged(type), $"{type.Name} must not contain references.");
        }
    }

    private static IEnumerable<string> FloatingPointUsages(Type type)
    {
        foreach (var field in type.GetFields(EveryMember))
        {
            if (UsesFloatingPoint(field.FieldType))
            {
                yield return $"{type.FullName}.{field.Name} (field)";
            }
        }

        foreach (var property in type.GetProperties(EveryMember))
        {
            if (UsesFloatingPoint(property.PropertyType))
            {
                yield return $"{type.FullName}.{property.Name} (property)";
            }
        }

        foreach (var method in type.GetMethods(EveryMember).Cast<MethodBase>().Concat(type.GetConstructors(EveryMember)))
        {
            var returnsFloat = method is MethodInfo info && UsesFloatingPoint(info.ReturnType);
            if (returnsFloat || method.GetParameters().Any(parameter => UsesFloatingPoint(parameter.ParameterType)))
            {
                yield return $"{type.FullName}.{method.Name} (signature)";
            }
        }
    }

    private static bool UsesFloatingPoint(Type type)
    {
        if (type.HasElementType)
        {
            return UsesFloatingPoint(type.GetElementType()!);
        }

        if (FloatingPointTypes.Contains(type))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(UsesFloatingPoint);
    }

    private static bool IsUnmanaged(Type type) =>
        type.IsPrimitive || type.IsEnum || type.IsPointer
        || (type.IsValueType && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .All(field => IsUnmanaged(field.FieldType)));
}
