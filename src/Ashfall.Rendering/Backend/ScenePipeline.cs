using System.Numerics;
using System.Runtime.InteropServices;
using Ashfall.Rendering.Scene;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Ashfall.Rendering.Backend;

/// <summary>
/// The lit quad pass (Shaders/Scene.slang): alpha-blended, no depth test (the scene arrives sorted back
/// to front), viewport and scissor dynamic so a resize never rebuilds the pipeline. One descriptor set
/// per frame in flight points the fragment shader at that frame's light buffer.
/// </summary>
internal sealed unsafe class ScenePipeline : IDisposable
{
    private readonly Device _device;

    public ScenePipeline(VulkanContext context, VkFormat colorFormat, int framesInFlight)
    {
        _device = context.Device;
        ColorFormat = colorFormat;

        var lightsBinding = new VkDescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = VkDescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = VkShaderStageFlags.FragmentBit,
        };
        DescriptorSetLayout = _device.CreateDescriptorSetLayout(
            new VkDescriptorSetLayoutCreateInfo { BindingCount = 1, Bindings = &lightsBinding },
            null);

        var poolSize = new VkDescriptorPoolSize { Type = VkDescriptorType.StorageBuffer, DescriptorCount = framesInFlight };
        DescriptorPool = _device.CreateDescriptorPool(
            new VkDescriptorPoolCreateInfo { MaxSets = framesInFlight, PoolSizeCount = 1, PoolSizes = &poolSize },
            null);

        var pushRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            Offset = (ByteSize32)0,
            Size = (ByteSize32)Marshal.SizeOf<SceneConstants>(),
        };
        var setLayout = DescriptorSetLayout;
        Layout = _device.CreatePipelineLayout(
            new ReadOnlySpan<VkDescriptorSetLayout>(&setLayout, 1),
            new ReadOnlySpan<VkPushConstantRange>(&pushRange, 1),
            null);

        Pipeline = CreatePipeline(colorFormat);
    }

    public VkFormat ColorFormat { get; }

    public VkPipeline Pipeline { get; }

    public VkPipelineLayout Layout { get; }

    public VkDescriptorSetLayout DescriptorSetLayout { get; }

    public VkDescriptorPool DescriptorPool { get; }

    public VkDescriptorSet AllocateSet()
    {
        var layout = DescriptorSetLayout;
        var set = default(VkDescriptorSet);
        _device.AllocateDescriptorSets(
            new VkDescriptorSetAllocateInfo { DescriptorPool = DescriptorPool, DescriptorSetCount = 1, SetLayouts = &layout },
            new ReadOnlySpan<VkDescriptorSet>(&set, 1));
        return set;
    }

    /// <summary>Points <paramref name="set"/> at <paramref name="lights"/> (call again after the buffer is reallocated).</summary>
    public void BindLights(VkDescriptorSet set, HostBuffer lights)
    {
        var bufferInfo = new VkDescriptorBufferInfo { Buffer = lights.Buffer, Offset = (ByteSize64)0, Range = (ByteSize64)(ulong)lights.Capacity };
        var write = new VkWriteDescriptorSet
        {
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = VkDescriptorType.StorageBuffer,
            BufferInfo = &bufferInfo,
        };
        _device.UpdateDescriptorSets(new ReadOnlySpan<VkWriteDescriptorSet>(&write, 1), []);
    }

    public void Dispose()
    {
        _device.DestroyPipeline(Pipeline, null);
        _device.DestroyPipelineLayout(Layout, null);
        _device.DestroyDescriptorPool(DescriptorPool, null);
        _device.DestroyDescriptorSetLayout(DescriptorSetLayout, null);
    }

    private VkPipeline CreatePipeline(VkFormat colorFormat)
    {
        var vertexModule = ShaderModuleExtensions.FromSpirv(_device, ShaderBytecode.Load(ShaderBytecode.SceneVertex), null);
        var fragmentModule = ShaderModuleExtensions.FromSpirv(_device, ShaderBytecode.Load(ShaderBytecode.SceneFragment), null);
        try
        {
            fixed (byte* entryPoint = "main\0"u8)
            {
                var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
                stages[0] = new VkPipelineShaderStageCreateInfo { Stage = VkShaderStageFlags.VertexBit, Module = vertexModule, Name = entryPoint };
                stages[1] = new VkPipelineShaderStageCreateInfo { Stage = VkShaderStageFlags.FragmentBit, Module = fragmentModule, Name = entryPoint };

                var binding = new VkVertexInputBindingDescription
                {
                    Binding = 0,
                    Stride = (ByteSize32)SceneVertex.SizeInBytes,
                    InputRate = VkVertexInputRate.Vertex,
                };
                var attributes = stackalloc VkVertexInputAttributeDescription[6];
                attributes[0] = Attribute(0, VkFormat.R32G32SFloat, nameof(SceneVertex.Screen));
                attributes[1] = Attribute(1, VkFormat.R32G32B32SFloat, nameof(SceneVertex.World));
                attributes[2] = Attribute(2, VkFormat.R32G32B32SFloat, nameof(SceneVertex.Normal));
                attributes[3] = Attribute(3, VkFormat.R32G32B32A32SFloat, nameof(SceneVertex.Color));
                attributes[4] = Attribute(4, VkFormat.R32G32SFloat, nameof(SceneVertex.Local));
                attributes[5] = Attribute(5, VkFormat.R32G32B32SFloat, nameof(SceneVertex.Extra));
                var vertexInput = new VkPipelineVertexInputStateCreateInfo
                {
                    VertexBindingDescriptionCount = 1,
                    VertexBindingDescriptions = &binding,
                    VertexAttributeDescriptionCount = 6,
                    VertexAttributeDescriptions = attributes,
                };

                var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { Topology = VkPrimitiveTopology.TriangleList };
                var viewport = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };
                var rasterization = new VkPipelineRasterizationStateCreateInfo
                {
                    PolygonMode = VkPolygonMode.Fill,
                    CullMode = VkCullModeFlags.None,
                    FrontFace = VkFrontFace.CounterClockwise,
                    LineWidth = 1f,
                };
                var multisample = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };
                var blendAttachment = new VkPipelineColorBlendAttachmentState
                {
                    BlendEnable = true,
                    SrcColorBlendFactor = VkBlendFactor.SrcAlpha,
                    DstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    ColorBlendOp = VkBlendOp.Add,
                    SrcAlphaBlendFactor = VkBlendFactor.One,
                    DstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    AlphaBlendOp = VkBlendOp.Add,
                    ColorWriteMask = VkColorComponentFlags.RBit | VkColorComponentFlags.GBit | VkColorComponentFlags.BBit | VkColorComponentFlags.ABit,
                };
                var colorBlend = new VkPipelineColorBlendStateCreateInfo { AttachmentCount = 1, Attachments = &blendAttachment };
                var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
                var dynamicState = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = 2, DynamicStates = dynamicStates };
                var format = colorFormat;
                var rendering = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &format };

                return _device.CreateGraphicsPipeline(
                    default,
                    new VkGraphicsPipelineCreateInfo
                    {
                        Next = &rendering,
                        StageCount = 2,
                        Stages = stages,
                        VertexInputState = &vertexInput,
                        InputAssemblyState = &inputAssembly,
                        ViewportState = &viewport,
                        RasterizationState = &rasterization,
                        MultisampleState = &multisample,
                        ColorBlendState = &colorBlend,
                        DynamicState = &dynamicState,
                        Layout = Layout,
                    },
                    null);
            }
        }
        finally
        {
            _device.DestroyShaderModule(vertexModule, null);
            _device.DestroyShaderModule(fragmentModule, null);
        }
    }

    private static VkVertexInputAttributeDescription Attribute(int location, VkFormat format, string field) => new()
    {
        Location = location,
        Binding = 0,
        Format = format,
        Offset = (ByteSize32)(int)Marshal.OffsetOf<SceneVertex>(field),
    };

    /// <summary>Push constants, laid out as Scene.slang's SceneConstants (std430).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SceneConstants
    {
        public Vector2 Viewport;
        public uint LightCount;
        public float Time;
        public Vector4 SunDirection;
        public Vector4 SunColor;
        public Vector4 Ambient;
    }
}
