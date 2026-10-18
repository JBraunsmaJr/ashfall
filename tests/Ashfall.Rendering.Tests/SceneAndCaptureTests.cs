using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using Ashfall.Rendering.Backend;
using Ashfall.Rendering.Scene;

namespace Ashfall.Rendering.Tests;

public class SceneAndCaptureTests
{
    [Fact]
    public void Scene_vertices_match_the_shader_input_layout()
    {
        // Scene.slang reads locations 0..5 as float2, float3, float3, float4, float2, float3 from one binding.
        Assert.Equal(68, SceneVertex.SizeInBytes);
        Assert.Equal(0, (int)Marshal.OffsetOf<SceneVertex>(nameof(SceneVertex.Screen)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceneVertex>(nameof(SceneVertex.World)));
        Assert.Equal(20, (int)Marshal.OffsetOf<SceneVertex>(nameof(SceneVertex.Normal)));
        Assert.Equal(32, (int)Marshal.OffsetOf<SceneVertex>(nameof(SceneVertex.Color)));
        Assert.Equal(48, (int)Marshal.OffsetOf<SceneVertex>(nameof(SceneVertex.Local)));
        Assert.Equal(56, (int)Marshal.OffsetOf<SceneVertex>(nameof(SceneVertex.Extra)));
        Assert.Equal(32, PointLight.SizeInBytes);
        Assert.Equal(64, Marshal.SizeOf<ScenePipeline.SceneConstants>());
    }

    [Theory]
    [InlineData(ShaderBytecode.SceneVertex)]
    [InlineData(ShaderBytecode.SceneFragment)]
    public void Precompiled_shaders_are_embedded_spirv(string name)
    {
        var code = ShaderBytecode.Load(name);

        Assert.Equal(0x07230203u, BitConverter.ToUInt32(code, 0));
        Assert.Equal(0x00010600u, BitConverter.ToUInt32(code, 4)); // SPIR-V 1.6, as Vulkan 1.3 consumes
    }

    [Fact]
    public void A_scene_grows_and_clears_without_losing_capacity()
    {
        var scene = new FrameScene();
        var vertex = new SceneVertex { Screen = new Vector2(1, 2) };
        for (var i = 0; i < 5_000; i++)
        {
            scene.AddQuad(vertex, vertex, vertex, vertex);
        }

        Assert.Equal(5_000, scene.QuadCount);
        Assert.Equal(20_000, scene.Vertices.Length);

        scene.Clear();
        Assert.Equal(0, scene.QuadCount);
        Assert.Throws<ArgumentException>(() => scene.AddQuads(new SceneVertex[3]));
    }

    [Fact]
    public void Lights_beyond_the_buffer_are_dropped()
    {
        var scene = new FrameScene();
        var light = new PointLight(Vector3.Zero, 1, LinearColor.Black, 1);
        for (var i = 0; i < FrameScene.MaxLights; i++)
        {
            Assert.True(scene.AddLight(light));
        }

        Assert.False(scene.AddLight(light));
        Assert.Equal(FrameScene.MaxLights, scene.Lights.Count);
    }

    [Fact]
    public void Screenshots_are_valid_png()
    {
        var rgba = new byte[3 * 2 * 4];
        rgba[0] = 255; // top-left pixel red
        rgba[^4] = 7;
        using var png = new MemoryStream();

        PngEncoder.Write(png, 3, 2, rgba);

        var bytes = png.ToArray();
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));

        // The image data inflates back to filter-byte-prefixed rows of the original pixels.
        var idatLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(33));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(bytes, 37, 4));
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(bytes, 41, idatLength), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        Assert.Equal(2 * (1 + (3 * 4)), raw.Length);
        Assert.Equal(255, raw[1]);
        Assert.Equal(7, raw[^4]);
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(bytes, bytes.Length - 8, 4));
    }

    [Fact]
    public void Captured_frames_index_pixels_row_by_row()
    {
        var rgba = new byte[2 * 2 * 4];
        rgba[((1 * 2) + 1) * 4] = 9;
        var frame = new CapturedFrame(new PixelSize(2, 2), rgba);

        Assert.Equal(((byte)9, (byte)0, (byte)0, (byte)0), frame.PixelAt(1, 1));
    }
}
