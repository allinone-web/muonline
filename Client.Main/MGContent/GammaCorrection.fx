#if SM6
    #define VS_SHADERMODEL vs_6_0
    #define PS_SHADERMODEL ps_6_0
#elif OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

#if SM6
float4 SampleSpriteTexture(float2 uv) { return SpriteTexture.Sample(SpriteTextureSampler, uv); }
#define tex2D(s, uv) SampleSpriteTexture(uv)
#define PS_COLOR SV_Target
#else
#define PS_COLOR COLOR
#endif

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : PS_COLOR
{
    float4 color = tex2D(SpriteTextureSampler, input.TextureCoordinates) * input.Color;
    
    color.rgb = saturate(color.rgb); // 0-1
    color.rgb = pow(abs(color.rgb), 2.2); // sRGB -> linear 
    
    return color;
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
