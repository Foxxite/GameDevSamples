using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SpaceDefence
{
    // Custom vertex type: all particle data is immutable after upload.
    // The vertex shader computes each particle's world position from CurrentTime.
    // Four of these vertices form one billboard quad (two triangles) per particle.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ParticleVertex : IVertexType
    {
        public Vector2 InitialPosition; // offset  0, 8 bytes
        public Vector2 Velocity;        // offset  8, 8 bytes
        public Vector2 Acceleration;    // offset 16, 8 bytes
        public Vector3 LifeParams;      // offset 24, 12 bytes  (lifespan, fade, halfExtent)
        public Vector2 CornerOffset;    // offset 36, 8 bytes   ([0,1]^2 quad corner)
        // Stride: 44 bytes

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0), // Velocity
            new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1), // Acceleration
            new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 2), // LifeParams
            new VertexElement(36, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 3)  // CornerOffset
        );

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }

    /// <summary>
    /// GPU-simulated particle emitter.  Replaces the old ParticleEmitter + N×Particle
    /// design with a single GameObject that lives on the CPU for the whole explosion.
    ///
    /// On emission a VertexBuffer and IndexBuffer are uploaded to the GPU once.
    /// Each frame the vertex shader computes every particle's current position
    /// with (p = p0 + v*t + 0.5*a*t^2), so the CPU does zero per-particle work.
    ///
    /// Draw order: GameManager.Draw() calls DrawGpu() directly before the main
    /// SpriteBatch pass, avoiding SpriteBatch interleaving entirely.
    /// </summary>
    public class GpuParticleEmitter : GameObject
    {
        // Shared across all instances - loaded once from ContentManager cache.
        private static Effect _shader;
        private static Texture2D _texture;

        private static readonly Vector2[] QuadCorners =
        {
            new Vector2(0, 0), // top-left
            new Vector2(1, 0), // top-right
            new Vector2(0, 1), // bottom-left
            new Vector2(1, 1), // bottom-right
        };
        // Two triangles per quad: TL,TR,BL  TR,BR,BL
        private static readonly short[] QuadIndices = { 0, 1, 2, 1, 3, 2 };

        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        private int _particleCount;
        private float _maxLifespan;
        private float _birthTime = -1f;  // -1 = not yet set
        private float _currentAge;

        private Vector2 _spawnPos;
        private readonly ParticleData _data;
        private readonly Random _rng = new Random();

        public GpuParticleEmitter(Vector2 position, ParticleData data)
        {
            _spawnPos = position;
            _data = data;
            CollisionType = CollisionType.None;

            // Dummy zero-radius collider so GetPosition() doesn't crash if called.
            SetCollider(new CircleCollider(position.X, position.Y, 0f));
        }

        public override void Load(ContentManager content)
        {
            _shader ??= content.Load<Effect>("GpuParticle");
            _texture ??= content.Load<Texture2D>("Particle");

            _particleCount = _data.particleCount;
            _maxLifespan = _data.lifespan;

            int vertCount = _particleCount * 4;
            int idxCount = _particleCount * 6;

            var vertices = new ParticleVertex[vertCount];
            var indices = new short[idxCount];

            float spriteHalf = _texture.Width * 0.5f;

            for (int i = 0; i < _particleCount; i++)
            {
                float dir = MathHelper.Lerp(_data.minDirection, _data.maxDirection,
                                              (float)_rng.NextDouble());
                float spd = MathHelper.Lerp(_data.minSpeed, _data.maxSpeed,
                                              (float)_rng.NextDouble());
                float scale = MathHelper.Lerp(_data.minScale, _data.maxScale,
                                              (float)_rng.NextDouble());

                Vector2 vel = new Vector2((float)Math.Cos(dir), (float)Math.Sin(dir)) * spd;
                float halfExt = spriteHalf * scale;
                var life = new Vector3(_data.lifespan, _data.fade, halfExt);

                for (int c = 0; c < 4; c++)
                {
                    vertices[i * 4 + c] = new ParticleVertex
                    {
                        InitialPosition = _spawnPos,
                        Velocity = vel,
                        Acceleration = _data.acceleration,
                        LifeParams = life,
                        CornerOffset = QuadCorners[c],
                    };
                }

                for (int q = 0; q < 6; q++)
                    indices[i * 6 + q] = (short)(i * 4 + QuadIndices[q]);
            }

            GraphicsDevice gd = GameManager.GetGameManager().GraphicsDevice;

            _vertexBuffer = new VertexBuffer(gd, ParticleVertex.VertexDeclaration,
                                             vertCount, BufferUsage.WriteOnly);
            _vertexBuffer.SetData(vertices);

            _indexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits,
                                           idxCount, BufferUsage.WriteOnly);
            _indexBuffer.SetData(indices);

            base.Load(content);
        }

        public override void Update(GameTime gameTime)
        {
            float now = (float)gameTime.TotalGameTime.TotalSeconds;

            if (_birthTime < 0f)
                _birthTime = now;

            _currentAge = now - _birthTime;

            if (_currentAge > _maxLifespan + _data.fade)
                GameManager.GetGameManager().RemoveGameObject(this);
        }

        // Called directly by GameManager.Draw() before the SpriteBatch pass
        // so we never have to End/Begin the SpriteBatch mid-frame.
        public void DrawGpu(GraphicsDevice gd, Matrix worldMatrix)
        {
            if (_vertexBuffer == null) return;

            Viewport vp = gd.Viewport;
            Matrix proj = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, -1);

            _shader.Parameters["MatrixTransform"].SetValue(worldMatrix * proj);
            _shader.Parameters["CurrentTime"].SetValue(_currentAge);
            _shader.Parameters["ParticleTexture"].SetValue(_texture);

            gd.SetVertexBuffer(_vertexBuffer);
            gd.Indices = _indexBuffer;

            foreach (EffectPass pass in _shader.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _particleCount * 2);
            }
        }

        // The general Draw() call from the SpriteBatch loop is intentionally a no-op.
        // All rendering goes through DrawGpu() above, called directly by GameManager.
        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch) { }

        public void Reset(Vector2 newPosition, ParticleData data)
        {
            _spawnPos = newPosition;   // make _spawnPos a settable field
            _birthTime = -1f;
            _currentAge = 0f;
            _maxLifespan = data.lifespan;

            // Re-upload new random particle data into the existing buffers
            int vertCount = _particleCount * 4;
            var vertices = new ParticleVertex[vertCount];
            float spriteHalf = _texture.Width * 0.5f;

            for (int i = 0; i < _particleCount; i++)
            {
                float dir = MathHelper.Lerp(data.minDirection, data.maxDirection, (float)_rng.NextDouble());
                float spd = MathHelper.Lerp(data.minSpeed, data.maxSpeed, (float)_rng.NextDouble());
                float scale = MathHelper.Lerp(data.minScale, data.maxScale, (float)_rng.NextDouble());
                Vector2 vel = new Vector2((float)Math.Cos(dir), (float)Math.Sin(dir)) * spd;
                float halfExt = spriteHalf * scale;
                var life = new Vector3(data.lifespan, data.fade, halfExt);

                for (int c = 0; c < 4; c++)
                    vertices[i * 4 + c] = new ParticleVertex
                    {
                        InitialPosition = newPosition,
                        Velocity = vel,
                        Acceleration = data.acceleration,
                        LifeParams = life,
                        CornerOffset = QuadCorners[c],
                    };
            }
            _vertexBuffer.SetData(vertices);   // reuse existing GPU buffer
        }

        public override void Destroy()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            base.Destroy();
        }
    }
}