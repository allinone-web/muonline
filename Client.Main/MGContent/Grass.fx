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

struct VS_IN
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 Tex      : TEXCOORD0;
    float4 Wind     : TEXCOORD1; // x=dirX, y=dirY, z=phase, w=amplitude
};

struct VS_OUT
{
    float4 Position          : SV_POSITION;
    float4 Color             : COLOR0;
    float2 Tex               : TEXCOORD0;
    float DensityVisibility  : TEXCOORD1;
};

VS_OUT GrassVS(VS_IN input)
{
    VS_OUT output;

    float sway = sin(Time * WindSpeed + input.Wind.z) * input.Wind.w * WindStrength;
    float4 worldPos = input.Position;
    worldPos.xy += input.Wind.xy * sway;

    float fadeRange = max(DensityFadeEnd - DensityFadeStart, 1.0f);
    float cameraDistance = distance(worldPos.xy, CameraPosition.xy);
    float density = saturate((DensityFadeEnd - cameraDistance) / fadeRange);
    density = density * density * (3.0f - 2.0f * density);

    // Vertex alpha stores a stable per-blade threshold. Every blade therefore fades at
    // a different distance, eliminating whole-chunk density switches while remaining
    // deterministic and free from temporal shimmer.
    output.DensityVisibility = density - input.Color.a;

    output.Position = mul(worldPos, World);
    output.Position = mul(output.Position, View);
    output.Position = mul(output.Position, Projection);
    output.Color = float4(input.Color.rgb, 1.0f);
    output.Tex = input.Tex;
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
