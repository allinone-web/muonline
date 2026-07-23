#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 World;
float4x4 View;
float4x4 Projection;

float Time;
float WindSpeed;
float WindStrength;
float AlphaCutoff;
float3 CameraPosition;
float DensityFadeStart;
float DensityFadeEnd;

texture GrassTexture;
sampler2D GrassSampler = sampler_state
{
    Texture = <GrassTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VS_OUT
{
    float4 Position          : SV_POSITION;
    float4 Color             : COLOR0;
    float2 Tex               : TEXCOORD0;
    float DensityVisibility  : TEXCOORD1;
};

float ComputeDensityVisibility(float2 worldPosition, float threshold)
{
    float fadeRange = max(DensityFadeEnd - DensityFadeStart, 1.0f);
    float cameraDistance = distance(worldPosition, CameraPosition.xy);
    float density = saturate((DensityFadeEnd - cameraDistance) / fadeRange);
    density = density * density * (3.0f - 2.0f * density);
    return density - threshold;
}

// -----------------------------------------------------------------------------
// Legacy expanded-vertex fallback. Kept for backends where instancing is not
// available or when an instanced draw fails at runtime.
// -----------------------------------------------------------------------------
struct VS_IN
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 Tex      : TEXCOORD0;
    float4 Wind     : TEXCOORD1;
};

VS_OUT GrassVS(VS_IN input)
{
    VS_OUT output;

    float sway = sin(Time * WindSpeed + input.Wind.z) * input.Wind.w * WindStrength;
    float4 worldPos = input.Position;
    worldPos.xy += input.Wind.xy * sway;

    output.DensityVisibility = ComputeDensityVisibility(worldPos.xy, input.Color.a);
    output.Position = mul(worldPos, World);
    output.Position = mul(output.Position, View);
    output.Position = mul(output.Position, Projection);
    output.Color = float4(input.Color.rgb, 1.0f);
    output.Tex = input.Tex;
    return output;
}

// -----------------------------------------------------------------------------
// Hardware-instanced path. Four shared template vertices are combined with one
// compact instance record per blade. The geometry and visual result match the
// legacy path, while VRAM and vertex fetch bandwidth are substantially lower.
// -----------------------------------------------------------------------------
struct VS_IN_INSTANCED
{
    float2 Corner          : POSITION0; // x=-1/+1, y=0/1
    float2 TemplateTex     : TEXCOORD0;
    float4 PositionHeights : TEXCOORD1; // centerXY, left/right base height
    float4 Shape           : TEXCOORD2; // halfWidth, height, cos, sin
    float4 Wind            : TEXCOORD3; // dirXY, phase, amplitude
    float4 UvLeanDensity   : TEXCOORD4; // u0, u1, lean, density threshold
    float4 InstanceColor   : COLOR1;
};

VS_OUT GrassInstancedVS(VS_IN_INSTANCED input)
{
    VS_OUT output;

    float side01 = input.Corner.x * 0.5f + 0.5f;
    float top = input.Corner.y;

    float2 widthAxis = float2(input.Shape.z, input.Shape.w);
    float2 endpoint = input.PositionHeights.xy + widthAxis * (input.Corner.x * input.Shape.x);
    float baseHeight = lerp(input.PositionHeights.z, input.PositionHeights.w, side01);

    float2 windDirection = input.Wind.xy;
    float2 leanedPosition = endpoint + windDirection * (input.UvLeanDensity.z * top);
    float sway = sin(Time * WindSpeed + input.Wind.z) * input.Wind.w * WindStrength * top;
    leanedPosition += windDirection * sway;

    float4 worldPos = float4(
        leanedPosition.x,
        leanedPosition.y,
        baseHeight + input.Shape.y * top,
        1.0f);

    output.DensityVisibility = ComputeDensityVisibility(worldPos.xy, input.UvLeanDensity.w);
    output.Position = mul(worldPos, World);
    output.Position = mul(output.Position, View);
    output.Position = mul(output.Position, Projection);
    output.Color = float4(input.InstanceColor.rgb, 1.0f);
    output.Tex = float2(
        lerp(input.UvLeanDensity.x, input.UvLeanDensity.y, side01),
        input.TemplateTex.y);
    return output;
}

float4 GrassPS(VS_OUT input) : SV_TARGET
{
    clip(input.DensityVisibility);

    float4 tex = tex2D(GrassSampler, input.Tex);
    clip(tex.a - AlphaCutoff);
    return float4(tex.rgb * input.Color.rgb, 1.0f);
}

technique Grass
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL GrassVS();
        PixelShader = compile PS_SHADERMODEL GrassPS();
    }
}

technique GrassInstanced
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL GrassInstancedVS();
        PixelShader = compile PS_SHADERMODEL GrassPS();
    }
}
