using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using SpaceDefence.Collision;
using System;
using System.Collections.Generic;

namespace SpaceDefence
{
	public class Ship : GameObject
	{
		public Vector2 Velocity { get; private set; }
		public float speed = 100;
		public float Range = 500;

		public float AvoidanceRange = 100;
		public float cooldown = 1;
		public float health = 100;

		private float _sqrtAvoidanceRange;
        private readonly List<GameObject> _nearbyBullets = new List<GameObject>();
        private readonly List<GameObject> _nearbyShips = new List<GameObject>();

        private double nextCheckForNearest = 0;
		private Ship cachedNearestEnemy = null;

		private Texture2D ship_body;
		private Texture2D base_turret;
		private Texture2D debug_pixel;

		private RectangleCollider _rectangleCollider;
		private Point target;
        private Color teamColor;

		private Effect recolorShader;
		private Matrix cachedProjection;
		private int cachedViewportWidth;
		private int cachedViewportHeight;

		private GameManager manager => GameManager.GetGameManager();

        public Color TeamColorValue => teamColor;
        public Effect ShaderEffect => recolorShader;

        /// <summary>
        /// The player character
        /// </summary>
        /// <param name="Position">The ship's starting position</param>
        public Ship(Point Position, CollisionType collisionType, Color teamColor)
		{
			_rectangleCollider = new RectangleCollider(new Rectangle(Position, Point.Zero));
			SetCollider(_rectangleCollider);
			CollisionType = collisionType | CollisionType.Solid;
			this.teamColor = teamColor;
		}

		public override void Load(ContentManager content)
		{
			// Original ship sprites from: https://zintoki.itch.io/space-breaker

			ship_body = content.Load<Texture2D>("ship_body");
			base_turret = content.Load<Texture2D>("base_turret");
			debug_pixel = content.Load<Texture2D>("pixel");

			_rectangleCollider.shape.Size = ship_body.Bounds.Size;
			_rectangleCollider.shape.Location -= new Point(ship_body.Width / 2, ship_body.Height / 2);

			recolorShader = content.Load<Effect>("RecolorShader");

			_sqrtAvoidanceRange = MathF.Sqrt(AvoidanceRange);

			base.Load(content);
		}

		public override void HandleInput(InputManager inputManager)
		{
			base.HandleInput(inputManager);
			if (inputManager.LeftMousePress())
			{
				Shoot();
			}
		}

		public override void OnCollision(GameObject other)
		{
			base.OnCollision(other);

			if (other is Bullet && (other.CollisionType & CollisionType) == 0)
			{
				health -= 1;
				if (health < 0)
				{
					manager.RemoveGameObject(this);
                    ParticleData data = new ParticleData
                    {
                        lifespan = 5,
                        particleCount = 40,
                        maxScale = .6f,
                        minScale = .2f
                    };
                    manager.AddGameObject(new GpuParticleEmitter(
                        GetPosition().Center.ToVector2(), data));
                }
			}
		}


		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

            Rectangle pos = GetPosition();
            Point center = pos.Center;

            cooldown -= (float)gameTime.ElapsedGameTime.TotalSeconds;

			Ship nearest = FindNearestEnemy(gameTime);
			target = nearest == null ? Point.Zero : nearest.GetPosition().Center;

			if ((target - center).ToVector2().LengthSquared() < Range * Range)
			{
				if (cooldown < 0)
				{
					_rectangleCollider.shape.Location += Shoot();
				}
			}
			else
			{
				_rectangleCollider.shape.Location += (Vector2.Normalize((target - center).ToVector2()) * speed * (float)gameTime.ElapsedGameTime.TotalSeconds).ToPoint();
			}

			_rectangleCollider.shape.Location += (AvoidObstacles() * (float)gameTime.ElapsedGameTime.TotalSeconds).ToPoint();
		}

        public Point Shoot()
        {
            cooldown = 0.5f;
            Vector2 aimDirection = LinePieceCollider.GetDirection(GetPosition().Center, target);
            Vector2 turretExit = _rectangleCollider.shape.Center.ToVector2() + aimDirection * base_turret.Height / 2f;

            // Rent a bullet from the pool. 
            manager.RentAndAddBullet(turretExit, aimDirection, 150, CollisionType);

            return (-aimDirection * 20).ToPoint();
        }

        public Vector2 AvoidObstacles()
		{
            if (manager.BulletSpatialHash.IsEmpty) return Vector2.Zero;

            Vector2 pos = GetPosition().Center.ToVector2();

			// Build a square query region that encloses the avoidance circle.
			// With CellSize = 150 and AvoidanceRange = 100, this touches at most
			// a 3×3 block of cells - regardless of how many bullets exist in the world.
			int range = (int)AvoidanceRange;

			Rectangle queryBounds = new Rectangle(
				(int)pos.X - range,
				(int)pos.Y - range,
				range * 2,
				range * 2);

			manager.BulletSpatialHash.QueryRegion(queryBounds, _nearbyBullets);

			Vector2 avoidance = Vector2.Zero;
			float avoidRangeSq = AvoidanceRange * AvoidanceRange;

			foreach (GameObject other in _nearbyBullets)
			{

				Vector2 difference = pos - other.GetPosition().Center.ToVector2();
				float distSq = difference.LengthSquared();

				// Cheap squared-distance rejection before computing any sqrt.
				if (distSq >= avoidRangeSq) continue;

				// We need dist^1.5 in the denominator:
				//   original: Normalize(diff) / sqrt(dist)
				//           = (diff / dist) / sqrt(dist)
				//           = diff / (dist * sqrt(dist))
				// Two sqrts total vs. three in the original (Length + Normalize + sqrt).
				float dist = MathF.Sqrt(distSq);
				float sqrtDist = MathF.Sqrt(dist);

				avoidance += _sqrtAvoidanceRange * speed * difference / (dist * sqrtDist);
			}

			return avoidance;
		}

        public Ship FindNearestEnemy(GameTime gameTime)
        {
            if (cachedNearestEnemy != null && gameTime.TotalGameTime.TotalMilliseconds < nextCheckForNearest)
                return cachedNearestEnemy;

            Ship nearest = null;
            Vector2 pos = GetPosition().Center.ToVector2();

            // Query the ship spatial hash within [Range] radius first.
            int rangeInt = (int)Range;
            Rectangle queryBounds = new Rectangle(
                (int)pos.X - rangeInt, (int)pos.Y - rangeInt,
                rangeInt * 2, rangeInt * 2);

            manager.ShipSpatialHash.QueryRegion(queryBounds, _nearbyShips);

            float bestDistSq = float.MaxValue;

            foreach (GameObject candidate in _nearbyShips)
            {
                Ship othership = (Ship)candidate;
                if ((othership.CollisionType & CollisionType.Teams) == (CollisionType & CollisionType.Teams))
                    continue;

                Vector2 newPos = othership.GetPosition().Center.ToVector2();
                float distSq = (pos - newPos).LengthSquared();

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = othership;
                }
            }

            // Fallback: if no enemy was found within Range (e.g. early in the match
            // when teams are far apart), do the original full list scan so ships
            // still move toward each other. This keeps functionality identical.
            bool usedFallback = (nearest == null);
            if (nearest == null)
            {
                var rawShips = manager.GetRawList(typeof(Ship));
                if (rawShips != null)
                    foreach (GameObject candidate in rawShips)
                    {
                        Ship othership = (Ship)candidate;
                        if ((othership.CollisionType & CollisionType.Teams) == (CollisionType & CollisionType.Teams))
                            continue;

                        Vector2 newPos = othership.GetPosition().Center.ToVector2();
                        float distSq = (pos - newPos).LengthSquared();

                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            nearest = othership;
                        }
                    }
            }

            cachedNearestEnemy = nearest;

            double cacheMs = usedFallback ? manager.RNG.Next(500, 1000) : manager.RNG.Next(33, 66);
            nextCheckForNearest = gameTime.TotalGameTime.TotalMilliseconds + cacheMs;
            return nearest;
        }

        public void DrawBatched(GameTime gameTime, SpriteBatch spriteBatch)
        {
            recolorShader.Parameters["HealthPercentage"].SetValue(health / 100f);

            spriteBatch.Draw(ship_body, _rectangleCollider.shape, Color.White);

            float aimAngle = LinePieceCollider.GetAngle(LinePieceCollider.GetDirection(GetPosition().Center, target));
            Rectangle turretLocation = base_turret.Bounds;
            turretLocation.Location = _rectangleCollider.shape.Center;
            spriteBatch.Draw(base_turret, turretLocation, null, Color.White, aimAngle,
                turretLocation.Size.ToVector2() / 2f, SpriteEffects.None, 0);
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Matrix worldMatrix)
		{
            // Debug draw the collider
            // spriteBatch.Begin(transformMatrix: worldMatrix);
            // spriteBatch.Draw(debug_pixel, _rectangleCollider.shape, Color.Yellow);
            // spriteBatch.End();

            recolorShader.Parameters["TeamColor"].SetValue(teamColor.ToVector4());
            recolorShader.Parameters["HealthPercentage"].SetValue(health / 100f);

            Viewport viewport = manager.GraphicsDevice.Viewport;
            if (cachedViewportWidth != viewport.Width || cachedViewportHeight != viewport.Height)
            {
                cachedViewportWidth = viewport.Width;
                cachedViewportHeight = viewport.Height;
                cachedProjection = Matrix.CreateOrthographicOffCenter(
                    0, viewport.Width,
                    viewport.Height, 0,
                    0, -1
                );
            }

            recolorShader.Parameters["MatrixTransform"].SetValue(worldMatrix * cachedProjection);

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, recolorShader);

            spriteBatch.Draw(ship_body, _rectangleCollider.shape, Color.White);

            float aimAngle = LinePieceCollider.GetAngle(LinePieceCollider.GetDirection(GetPosition().Center, target));
            Rectangle turretLocation = base_turret.Bounds;
            turretLocation.Location = _rectangleCollider.shape.Center;

            spriteBatch.Draw(base_turret, turretLocation, null, Color.White, aimAngle, turretLocation.Size.ToVector2() / 2f, SpriteEffects.None, 0);

            spriteBatch.End();
        }
	}
}
