using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using SpaceDefence.Collision;
using SpaceDefence.Engine;

namespace SpaceDefence
{
	public class GameManager
	{
		private static GameManager gameManager;

		private FPSCounter counter;

		private readonly Dictionary<Type, List<GameObject>> _gameObjectsByType = new();

		// Flat list kept in sync with _gameObjectsByType.
		// Replaces the per-call SelectMany+ToList() allocation in GetGameObjects().
		private readonly List<GameObject> _allGameObjects = new List<GameObject>();

		// Broad-phase spatial hash; rebuilt every CheckCollision() call.
		private readonly SpatialHash _spatialHash = new SpatialHash();

		// Bullet-only spatial hash used by Ship.AvoidObstacles().
		// Rebuilt once at the start of Update(), before any ship Update() runs,
		// so all ships query consistent bullet positions for the frame.
		private readonly SpatialHash _bulletSpatialHash = new SpatialHash();

		/// <summary>Read-only access to the bullet spatial hash for Ship.AvoidObstacles().</summary>
		public SpatialHash BulletSpatialHash => _bulletSpatialHash;

        // Ship-only spatial hash used by Ship.FindNearestEnemy().
        // Rebuilt once per Update() so all ships see a consistent snapshot.
        private readonly SpatialHash _shipSpatialHash = new SpatialHash();

        /// <summary>Read-only access to the ship spatial hash for Ship.FindNearestEnemy().</summary>
        public SpatialHash ShipSpatialHash => _shipSpatialHash;

        private List<GameObject> _toBeRemoved;
		private List<GameObject> _toBeAdded;
		private ContentManager _content;

		// Debug draw resources
		private Texture2D _debugPixel;
		public bool DebugDrawQuadTree { get; set; } = true;

		public Matrix WorldMatrix { get; set; }

		public Random RNG { get; private set; }
		public InputManager InputManager { get; private set; }
		public Game Game { get; private set; }

		public GraphicsDevice GraphicsDevice => Game.GraphicsDevice;

		public static GameManager GetGameManager()
		{
			if (gameManager == null)
				gameManager = new GameManager();
			return gameManager;
		}

		public GameManager()
		{
			_toBeRemoved = new List<GameObject>();
			_toBeAdded = new List<GameObject>();

			InputManager = new InputManager();
			RNG = new Random();

			//WorldMatrix = Matrix.CreateScale(.3f);
			WorldMatrix = Matrix.CreateScale(0.8f) * Matrix.CreateTranslation(0, -600, 0);
		}

		public void Initialize(ContentManager content, Game game)
		{
			Game = game;
			_content = content;
		}

		public void Load(ContentManager content)
		{
			// Snapshot what's in the flat list before clearing the type cache,
			// then reload everything through AddToTypeCache so both stay in sync.
			var snapshot = new List<GameObject>(_allGameObjects);
			_gameObjectsByType.Clear();
			_allGameObjects.Clear();

			foreach (GameObject gameObject in snapshot)
			{
				gameObject.Load(content);
				AddToTypeCache(gameObject);   // re-populates both collections
			}
			counter = new FPSCounter(content.Load<SpriteFont>("Font"));

			// Load a 1x1 pixel texture used for debug drawing of the quadtree
			try
			{
				_debugPixel = content.Load<Texture2D>("pixel");
			}
			catch
			{
				// If pixel not present, leave null - debug drawing will be skipped.
				_debugPixel = null;
			}
		}

		public void HandleInput(InputManager inputManager)
		{
			foreach (GameObject gameObject in GetGameObjects())
			{
				gameObject.HandleInput(this.InputManager);
			}
		}

		public void CheckCollision()
		{
			// ── Broad phase: build the spatial hash ──────────────────────────────
			_spatialHash.Clear();
			foreach (GameObject obj in _allGameObjects)
			{
				// Objects with no collision type or no collider can't collide.
				if (obj.CollisionType == CollisionType.None) continue;
				if (obj.collider == null) continue;
				_spatialHash.Insert(obj);
			}

			// ── Narrow phase: check only spatially adjacent pairs ────────────────
			_spatialHash.QueryPairs((objA, objB) =>
			{
				// Skip pairs that are on the same team / share a collision bit -
				// same logic as the original brute-force check.
				if ((objA.CollisionType & objB.CollisionType) != 0)
					return;

				if (objA.CheckCollision(objB))
				{
					objA.OnCollision(objB);
					objB.OnCollision(objA);
				}
			});
		}

		public void Update(GameTime gameTime)
		{
			InputManager.Update();
			// Handle input
			HandleInput(InputManager);

			// Rebuild the bullet hash from current bullet positions so that every
			// ship queries the same snapshot when AvoidObstacles() runs below.
			_bulletSpatialHash.Clear();
			if (_gameObjectsByType.TryGetValue(typeof(Bullet), out List<GameObject> bulletList))
			{
				foreach (GameObject bullet in bulletList)
					_bulletSpatialHash.Insert(bullet);
			}

            // Rebuild the ship spatial hash once per frame so that every ship
            // can do a cheap local query in FindNearestEnemy() instead of scanning
            // the full enemy list.
            _shipSpatialHash.Clear();
            if (_gameObjectsByType.TryGetValue(typeof(Ship), out List<GameObject> shipList))
            {
                foreach (GameObject ship in shipList)
                    _shipSpatialHash.Insert(ship);
            }

            // Update
            foreach (GameObject gameObject in GetGameObjects())
			{
				gameObject.Update(gameTime);
			}

			// Check Collission
			CheckCollision();

			foreach (GameObject gameObject in _toBeAdded)
			{
				gameObject.Load(_content);
				AddToTypeCache(gameObject);
			}
			_toBeAdded.Clear();

			foreach (GameObject gameObject in _toBeRemoved)
			{
				gameObject.Destroy();
				RemoveFromTypeCache(gameObject);
			}
			_toBeRemoved.Clear();
		}

		public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
		{
			if (GetGameObjectsByType(typeof(Ship)) is List<GameObject> ships)
			{
				foreach (Ship ship in ships)
				{
					ship.Draw(gameTime, spriteBatch, WorldMatrix);
				}
			}

			spriteBatch.Begin(transformMatrix: WorldMatrix);
			foreach (var typeBucket in _gameObjectsByType)
			{
				if (typeBucket.Key == typeof(Ship))
					continue;

				foreach (GameObject gameObject in typeBucket.Value)
				{
					gameObject.Draw(gameTime, spriteBatch);
				}
			}
			spriteBatch.End();

			spriteBatch.Begin();
			counter.Draw(gameTime, spriteBatch);
			spriteBatch.End();
		}

		/// <summary>
		/// Add a new GameObject to the GameManager. 
		/// The GameObject will be added at the start of the next Update step. 
		/// Once it is added, the GameManager will ensure all steps of the game loop will be called on the object automatically. 
		/// </summary>
		/// <param name="gameObject"> The GameObject to add. </param>
		public void AddGameObject(GameObject gameObject)
		{
			_toBeAdded.Add(gameObject);
		}

		/// <summary>
		/// Remove GameObject from the GameManager. 
		/// The GameObject will be removed at the start of the next Update step and its Destroy() mehtod will be called.
		/// After that the object will no longer receive any updates.
		/// </summary>
		/// <param name="gameObject"> The GameObject to Remove. </param>
		public void RemoveGameObject(GameObject gameObject)
		{
			_toBeRemoved.Add(gameObject);
		}

		private void AddToTypeCache(GameObject gameObject)
		{
			Type type = gameObject.GetType();
			if (!_gameObjectsByType.TryGetValue(type, out List<GameObject> typeObjects))
			{
				typeObjects = new List<GameObject>();
				_gameObjectsByType[type] = typeObjects;
			}

			typeObjects.Add(gameObject);
			_allGameObjects.Add(gameObject);   // keep flat list in sync
		}

		private void RemoveFromTypeCache(GameObject gameObject)
		{
			Type type = gameObject.GetType();
			if (!_gameObjectsByType.TryGetValue(type, out List<GameObject> typeObjects))
			{
				return;
			}

			typeObjects.Remove(gameObject);
			_allGameObjects.Remove(gameObject);   // keep flat list in sync

			if (typeObjects.Count == 0)
			{
				_gameObjectsByType.Remove(type);
			}
		}

		public List<GameObject> GetGameObjects()
		{
			// Returns the maintained flat list - no allocation, no LINQ.
			return _allGameObjects;
		}

		public List<GameObject> GetGameObjectsByType(Type type)
		{
			if (_gameObjectsByType.TryGetValue(type, out List<GameObject> typeObjects))
			{
				return typeObjects;
			}

			return new List<GameObject>();
		}

		/// <summary>
		/// Get a random location on the screen.
		/// </summary>
		public Vector2 RandomScreenLocation()
		{
			return new Vector2(
				RNG.Next(0, Game.GraphicsDevice.Viewport.Width),
				RNG.Next(0, Game.GraphicsDevice.Viewport.Height));
		}
	}
}