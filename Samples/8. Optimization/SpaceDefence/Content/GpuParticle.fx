// GpuParticle.fx
// GPU-simulated particle system.
//
// All N particles for one explosion are stored as a single vertex/index buffer
// uploaded ONCE at emit time.  The vertex shader computes each particle's
// world-space position analytically from the uniform CurrentTime, so zero
// CPU work is needed per particle per frame.

float4x4 MatrixTransform; // WorldMatrix * Projection, set once per emitter draw call
float    CurrentTime;     // Seconds since this emitter was born (increases each frame)

texture2D ParticleTexture;
sampler2D ParticleSampler = sampler_state
{
    Texture   = <ParticleTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

// Per-vertex data: all fields are IMMUTABLE after upload.
// The vertex buffer is written once on Emit(); the shader does all motion math.
struct VertexInput
{
    float2 InitialPosition : POSITION0;  // world-space spawn point
    float2 Velocity        : TEXCOORD0;  // world pixels / second
    float2 Acceleration    : TEXCOORD1;  // world pixels / second^2
    float3 LifeParams      : TEXCOORD2;  // x=lifespan(s), y=fade(s), z=halfExtent(px)
    float2 CornerOffset    : TEXCOORD3;  // [0,1]^2 -- which corner of the billboard quad
};

struct VertexOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
    float  Alpha    : TEXCOORD1;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput o;

    float lifespan = input.LifeParams.x;
    float fade     = input.LifeParams.y;
    float halfExt  = input.LifeParams.z;
    float t        = CurrentTime;

    // Cull dead/unborn particles by pushing into the discard zone.
    if (t < 0 || t > lifespan)
    {
        o.Position = float4(2, 2, 2, 1); // outside clip volume -> clipped
        o.TexCoord = float2(0, 0);
        o.Alpha    = 0;
        return o;
    }

    // Kinematic simulation: p(t) = p0 + v*t + 0.5*a*t^2
    // This is the same physics the old Particle.Update() did on the CPU,
    // now executed in parallel on the GPU for all particles at once.
    float2 center = input.InitialPosition
                  + input.Velocity      * t
                  + 0.5 * input.Acceleration * (t * t);

    // Expand the point particle to an axis-aligned billboard quad.
    // CornerOffset is in [0,1]^2; remap to [-1,+1]^2 and scale by halfExtent.
    float2 worldPos = center + (input.CornerOffset * 2.0 - 1.0) * halfExt;

    o.Position = mul(float4(worldPos, 0, 1), MatrixTransform);
    o.TexCoord = input.CornerOffset;

    // Alpha fades from 1 -> 0 during the last `fade` seconds of the particle's life.
    float timeLeft = lifespan - t;
    o.Alpha = saturate(timeLeft / max(fade, 0.0001));

    return o;
}

float4 PS(VertexOutput input) : COLOR
{
    float4 col = tex2D(ParticleSampler, input.TexCoord);
    col.a *= input.Alpha;
    return col;
}

technique GpuParticle
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
