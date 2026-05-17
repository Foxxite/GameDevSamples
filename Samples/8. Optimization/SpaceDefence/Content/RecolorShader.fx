#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

matrix MatrixTransform;

// Input texture
texture Texture;

float HealthPercentage;
float4 TeamColor;

// Sampler
sampler TextureSampler = sampler_state
{
    Texture = <Texture>;
};

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput MainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput) 0;

    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;

    return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 texColor = tex2D(TextureSampler, input.TexCoord);

	// Detect pixels where:
	// R == B
	// G == 0
	// R != 0
	//
	// Using small tolerance because floats are not exact.
    float tolerance = 0.01;

    bool isTeamPixel =
		abs(texColor.r - texColor.b) < tolerance &&
		texColor.g < tolerance &&
		texColor.r > tolerance;

    if (isTeamPixel)
    {
        float originalShade = texColor.r;

        float3 finalColor =
			TeamColor.rgb *
			HealthPercentage *
			originalShade;

        return float4(finalColor, texColor.a) * input.Color;
    }

    return texColor * input.Color;
}

technique BasicColorDrawing
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};