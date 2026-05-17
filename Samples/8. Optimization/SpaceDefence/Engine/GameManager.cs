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

		private List<GameObject> _toBeRemoved;
		private List<GameObject> _toBeAdded;
		private ContentManager _content;

		private readonly QuadTree _quadTree = new(0, new Rectangle(0, 0, 1920, 1080));
		private readonly List<GameObject> _nearby = new(); // Reused every frame

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
			_gameObjectsByType.Clear();
			foreach (GameObject gameObject in GetGameObjects())
			{
				gameObject.Load(content);
				AddToTypeCache(gameObject);
			}
			counter = new FPSCounter(content.Load<SpriteFont>("Font"));
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
			var gameObjects = GetGameObjects();

			_quadTree.Clear();
			foreach (var obj in gameObjects)
				if (obj.CollisionType != CollisionType.None)
					_quadTree.Insert(obj);

			int count = gameObjects.Count;
			for (int i = 0; i < count; i++)
			{
				var objA = gameObjects[i];
				if (objA.CollisionType == CollisionType.None) continue;

				_nearby.Clear();
				_quadTree.Retrieve(_nearby, objA.collider.GetBoundingBox());

				foreach (var objB in _nearby)
				{
					if (objB == objA) continue;
					if ((objA.CollisionType & objB.CollisionType) == CollisionType.None) continue;

					if (objA.CheckCollision(objB))
					{
						objA.OnCollision(objB);
						objB.OnCollision(objA);
					}
				}
			}
		}

		public void Update(GameTime gameTime)
		{
			InputManager.Update();
			// Handle input
			HandleInput(InputManager);


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
		}

		private void RemoveFromTypeCache(GameObject gameObject)
		{
			Type type = gameObject.GetType();
			if (!_gameObjectsByType.TryGetValue(type, out List<GameObject> typeObjects))
			{
				return;
			}

			typeObjects.Remove(gameObject);
			if (typeObjects.Count == 0)
			{
				_gameObjectsByType.Remove(type);
			}
		}

		public List<GameObject> GetGameObjects()
		{
			return _gameObjectsByType.Values.SelectMany(list => list).ToList();
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
