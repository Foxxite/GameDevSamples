using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SpaceDefence
{
    public class Bullet : GameObject
    {
        private Texture2D _texture;
        private CircleCollider _circleCollider;
        private Vector2 _velocity;
        public float bulletSize = 4;
        public float LifeTime = 3;

        // Tracks whether this pool slot is currently in-flight.
        // Set to true by GameManager.RentAndAddBullet(), false by ReturnBullet().
        public bool IsActive = false;

        // Pre-allocated constructor used by GameManager to fill the bullet pool.
        // All pool slots are created at startup; Reset() initialises them for use.
        public Bullet()
        {
            _circleCollider = new CircleCollider(Vector2.Zero, bulletSize);
            SetCollider(_circleCollider);
            CollisionType = CollisionType.None; // overwritten in Reset()
        }

        // Legacy constructor kept for any call sites outside the pool path.
        public Bullet(Vector2 location, Vector2 direction, float speed, CollisionType collisionType)
        {
            CollisionType = collisionType & ~CollisionType.Solid;
            _circleCollider = new CircleCollider(location, bulletSize);
            SetCollider(_circleCollider);
            _velocity = direction * speed;
        }

        /// <summary>
        /// Re-initialise this bullet for reuse from the ring buffer.
        /// Called by GameManager.RentAndAddBullet() before the bullet is
        /// added to (or left in) the active game-object lists.
        /// </summary>
        public void Reset(Vector2 location, Vector2 direction, float speed, CollisionType collisionType)
        {
            CollisionType = collisionType & ~CollisionType.Solid;
            _circleCollider.Center = location;
            _circleCollider.Radius = bulletSize;
            _velocity = direction * speed;
            LifeTime = 3f;
        }

        public override void Load(ContentManager content)
        {
            _texture ??= content.Load<Texture2D>("Bullet");
            base.Load(content);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            _circleCollider.Center += _velocity * (float)gameTime.ElapsedGameTime.TotalSeconds;
            LifeTime -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (LifeTime < 0)
            {
                GameManager mgr = GameManager.GetGameManager();
                mgr.RemoveGameObject(this);
                mgr.ReturnBullet(this); // mark slot free for pool reuse
            }
        }

        public override void OnCollision(GameObject other)
        {
            base.OnCollision(other);
            if (other is Ship && (other.CollisionType & CollisionType) == 0)
            {
                GameManager mgr = GameManager.GetGameManager();
                mgr.RemoveGameObject(this);
                mgr.ReturnBullet(this);

                ParticleData data = new ParticleData();
                data.maxScale = 0.2f;
                data.minScale = 0.1f;
                mgr.AddGameObject(new GpuParticleEmitter(
                    GetPosition().Center.ToVector2(), data));
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_texture, _circleCollider.GetBoundingBox(), Color.Red);
            base.Draw(gameTime, spriteBatch);
        }
    }
}
