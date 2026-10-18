namespace Ashfall.Rendering.Backend;

/// <summary>
/// Precompiled SPIR-V shipped inside the assembly. Sources are Shaders/*.slang; eng/compile-shaders.sh
/// rebuilds the .spv files with slangc, so building the game never needs a shader compiler.
/// </summary>
internal static class ShaderBytecode
{
    public const string SceneVertex = "Scene.vert.spv";
    public const string SceneFragment = "Scene.frag.spv";

    private const uint SpirvMagic = 0x07230203;

    public static byte[] Load(string name)
    {
        var assembly = typeof(ShaderBytecode).Assembly;
        using var stream = assembly.GetManifestResourceStream("Ashfall.Rendering.Shaders." + name)
            ?? throw new InvalidOperationException($"Shader '{name}' is not embedded in {assembly.GetName().Name}.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        if (bytes.Length < 20 || bytes.Length % 4 != 0 || BitConverter.ToUInt32(bytes, 0) != SpirvMagic)
        {
            throw new InvalidOperationException($"Shader '{name}' is not SPIR-V.");
        }

        return bytes;
    }
}
