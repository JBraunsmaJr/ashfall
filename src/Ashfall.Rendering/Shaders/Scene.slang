// Ashfall scene shader (M2): every world object, effect and UI element is a batch of lit quads.
//
// The CPU projects geometry to screen pixels (orthographic 2:1 dimetric) and sorts it back to front,
// so the GPU only maps pixels to clip space, shapes discs and rings, and lights each pixel:
// ambient + one directional sun + point lights (muzzle flashes, base lights), then applies the
// fog of war. This is the M2 lighting prototype; the sprite G-buffer and deferred pass replace it once
// the content cooker produces normal-mapped atlases.
//
// Compiled ahead of time to SPIR-V (see eng/compile-shaders.sh); the .spv files are embedded resources.

struct VertexInput
{
    [[vk::location(0)]] float2 screen;   // pixels, origin top-left
    [[vk::location(1)]] float3 world;    // cells (x, y) and height above ground, for lighting
    [[vk::location(2)]] float3 normal;   // world-space surface normal
    [[vk::location(3)]] float4 color;    // linear RGB, straight alpha
    [[vk::location(4)]] float2 local;    // -1..1 across the quad, for disc and ring shapes
    [[vk::location(5)]] float3 extra;    // x: shape (0 solid, 1 disc, 2 ring, 3 glow), y: emissive 0..1, z: visibility 0..1
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 world;
    float3 normal;
    float4 color;
    float2 local;
    float3 extra;
};

struct PointLight
{
    float4 positionRadius;   // xyz in cells, w radius in cells
    float4 colorIntensity;   // rgb linear, a intensity
};

struct SceneConstants
{
    float2 viewport;
    uint lightCount;
    float time;
    float4 sunDirection;   // xyz: unit vector towards the sun
    float4 sunColor;       // rgb * intensity
    float4 ambient;        // rgb
};

[[vk::push_constant]] ConstantBuffer<SceneConstants> scene;
[[vk::binding(0, 0)]] StructuredBuffer<PointLight> lights;

[shader("vertex")]
VertexOutput vertexMain(VertexInput input)
{
    VertexOutput output;
    float2 ndc = input.screen / scene.viewport * 2.0 - 1.0;
    output.position = float4(ndc, 0.0, 1.0);
    output.world = input.world;
    output.normal = input.normal;
    output.color = input.color;
    output.local = input.local;
    output.extra = input.extra;
    return output;
}

float3 lightSurface(float3 albedo, float3 world, float3 normal)
{
    float3 n = normalize(normal);
    float3 light = scene.ambient.rgb + scene.sunColor.rgb * max(dot(n, scene.sunDirection.xyz), 0.0);
    for (uint i = 0; i < scene.lightCount; i++)
    {
        PointLight point = lights[i];
        float3 toLight = point.positionRadius.xyz - world;
        float distance = length(toLight);
        float falloff = saturate(1.0 - distance / point.positionRadius.w);
        if (falloff > 0.0)
        {
            // Wrapped diffuse: flat ground near a light still catches some of it.
            float facing = saturate(dot(n, toLight / max(distance, 0.001)) * 0.7 + 0.3);
            light += point.colorIntensity.rgb * point.colorIntensity.a * falloff * falloff * facing;
        }
    }

    return albedo * light;
}

// Shroud is black; fog is a dim, desaturated version of the scene; visible is full colour.
float3 applyFog(float3 color, float visibility)
{
    float grey = dot(color, float3(0.2126, 0.7152, 0.0722));
    float3 fogged = lerp(float3(grey, grey, grey), color, 0.35) * 0.45;
    return visibility < 0.5
        ? fogged * (visibility * 2.0)
        : lerp(fogged, color, (visibility - 0.5) * 2.0);
}

[shader("fragment")]
float4 fragmentMain(VertexOutput input) : SV_Target
{
    float alpha = input.color.a;
    float shape = input.extra.x;
    if (shape > 2.5)
    {
        // Glow: a soft radial falloff for flashes, fireballs and smoke.
        float radius = length(input.local);
        float falloff = saturate(1.0 - radius * radius);
        alpha *= falloff * falloff;
        if (alpha <= 0.002)
        {
            discard;
        }
    }
    else if (shape > 0.5)
    {
        float radius = length(input.local);
        float edge = fwidth(radius) + 0.0001;
        alpha *= 1.0 - smoothstep(1.0 - edge, 1.0, radius);
        if (shape > 1.5)
        {
            alpha *= smoothstep(0.72 - edge, 0.72, radius);
        }

        if (alpha <= 0.002)
        {
            discard;
        }
    }

    float3 lit = lightSurface(input.color.rgb, input.world, input.normal);
    float3 color = lerp(lit, input.color.rgb, input.extra.y);
    return float4(applyFog(color, input.extra.z), alpha);
}
