// AlphaRGB.fx (MonoGame cross-platform)
// - WindowsDX (SM4): Texture2D/SamplerState + ps_4_0_level_9_1
// - DesktopGL/OpenGL: sampler2D/tex2D + ps_3_0

float4x4 WorldViewProjection;

#if SM4 || SM6
// -------------------- DX11 / WindowsDX --------------------
Texture2D TextureSampler : register(t0);
SamplerState PointClamp : register(s0)
{
    Filter   = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VS_IN  { float3 Pos:POSITION0; float2 Tex:TEXCOORD0; };
struct VS_OUT { float4 Pos:SV_POSITION; float2 Tex:TEXCOORD0; };

VS_OUT VS_Main(VS_IN v)
{
    VS_OUT o;
    o.Pos = mul(float4(v.Pos, 1), WorldViewProjection);
    o.Tex = v.Tex;
    return o;
}

float4 PS_Main(VS_OUT pin) : SV_Target
{
    float4 col = TextureSampler.Sample(PointClamp, pin.Tex);

    float a = saturate(dot(col.rgb, float3(1.3333333, 1.3333333, 1.3333333)));
    return float4(col.rgb, a);
}

#if SM6
    #define ALPHA_VS_MODEL vs_6_0
    #define ALPHA_PS_MODEL ps_6_0
#else
    #define ALPHA_VS_MODEL vs_4_0_level_9_1
    #define ALPHA_PS_MODEL ps_4_0_level_9_1
#endif

technique AlphaRGB
{
    pass P0
    {
        VertexShader = compile ALPHA_VS_MODEL VS_Main();
        PixelShader  = compile ALPHA_PS_MODEL PS_Main();
    }
}

#else
// -------------------- OpenGL / DesktopGL (XNA-style) --------------------
sampler TextureSampler : register(s0);

struct VS_IN  { float4 Pos:POSITION0; float2 Tex:TEXCOORD0; };
struct VS_OUT { float4 Pos:POSITION0; float2 Tex:TEXCOORD0; };

VS_OUT VS_Main(VS_IN v)
{
    VS_OUT o;
    o.Pos = mul(v.Pos, WorldViewProjection);
    o.Tex = v.Tex;
    return o;
}

float4 PS_Main(VS_OUT pin) : COLOR0
{
    float4 col = tex2D(TextureSampler, pin.Tex);

    float a = saturate(dot(col.rgb, float3(1.3333333, 1.3333333, 1.3333333)));
    return float4(col.rgb, a);
}

technique AlphaRGB
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS_Main();
        PixelShader  = compile ps_3_0 PS_Main();
    }
}
#endif
