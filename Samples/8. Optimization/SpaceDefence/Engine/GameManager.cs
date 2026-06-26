using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using SpaceDefence.Engine;
using
System;
using System.Collections.Concurrent;
using
System.Collections.Generic;
using System.Linq;
using
System.Threading.Tasks;

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

        private ConcurrentQueue<GameObject> _toBeRemoved;
        private ConcurrentQueue<GameObject> _toBeAdded;
        private ContentManager _content;

        // Debug draw resources
        private Texture2D _debugPixel;
        public bool DebugDrawQuadTree { get; set; } = true;

        public Matrix WorldMatrix { get; set; }

        public Random RNG { get; private set; }
        public InputManager InputManager { get; private set; }
        public Game Game { get; private set; }

        public GraphicsDevice GraphicsDevice => Game.GraphicsDevice;

        private const int BulletPoolCap = 8000;
        private readonly Bullet[] _bulletPool = new Bullet[BulletPoolCap];
        private int _bulletPoolNext = 0;

        // ── Clump-leader election ─────────────────────────────────────────────
        // Maps a packed (team | cellX | cellY) key to the elected leader ship
        // for that cell.  Rebuilt every frame on the main thread before the
        // parallel ship update, so no locking is needed.
        private readonly Dictionary<long, Ship> _clumpCellLeaders = new();

        // A grid cell of 150 px means ships within ~1 ship-length of each other
        // share a leader.  Increase to group larger clumps; decrease for tighter
        // per-ship accuracy.
        private const int ClumpCellSize = 300;

        // Re-elect clump leaders only once every N frames.  Ships move ~100 px/s
        // so at 60 fps they travel ~1.7 px per frame; over 10 frames that is ~17 px —
        // well within one clump cell (150 px).  Raise this to save more CPU;
        // lower it if ships feel like they stop reacting to formation changes.
        private const int ClumpElectionInterval = 30;
        private int _clumpElectionCountdown = 0;
        // ─────────────────────────────────────────────────────────────────────

        public static GameManager GetGameManager()
        {
            if (gameManager == null)
                gameManager = new GameManager();
            return gameManager;
        }

        public GameManager()
        {
            _toBeRemoved = new ConcurrentQueue<GameObject>();
            _toBeAdded = new ConcurrentQueue<GameObject>();

            InputManager = new InputManager();
            RNG = new Random();

            //Setup the spacial hash sizes
            _shipSpatialHash.CellSize = 150;
            _bulletSpatialHash.CellSize = 15;

            //WorldMatrix = Matrix.CreateScale(.3f);
            WorldMatrix = Matrix.CreateScale(0.8f) * Matrix.CreateTranslation(0, -600, 0);

            // Pre-allocate all bullet slots up front so the ring never heap-allocates.
            for (int i = 0; i < BulletPoolCap; i++)
                _bulletPool[i] = new Bullet();
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

        /// <summary>Rent a bullet from the pool (or reset the oldest one).</summary>
        /// <summary>
        /// Rent a bullet from the fixed-size ring buffer and register it with the
        /// game.  The caller must NOT call AddGameObject separately.
        ///
        /// If the ring has wrapped (> 2000 simultaneous bullets) the oldest
        /// in-flight bullet is reset to the new parameters.
        /// </summary>
        public Bullet RentAndAddBullet(Vector2 location, Vector2 direction,
                                       float speed, CollisionType collisionType)
        {
            Bullet b = _bulletPool[_bulletPoolNext];
            _bulletPoolNext = (_bulletPoolNext + 1) % BulletPoolCap;

            // A bullet that was returned this frame is in _toBeRemoved (still in
            // _allGameObjects) but has IsActive = false.  Detect both cases:
            bool inGame = b.IsActive;

            if (inGame)
            {
                // Bullet is still registered: reset in-place.
                b.Reset(location, direction, speed, collisionType);
                b.IsActive = true;
                // b is already in _allGameObjects - no AddGameObject needed.
            }
            else
            {
                // Slot is genuinely free: reset and queue for deferred add.
                b.Reset(location, direction, speed, collisionType);
                b.IsActive = true;
                AddGameObject(b);
            }

            return b;
        }

        /// <summary>Mark a bullet as available for reuse on the next ring wrap.</summary>
        public void ReturnBullet(Bullet b) => b.IsActive = false;


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
                if (obj is Bullet && !obj.IsActive) continue;
                if (obj.CollisionType == CollisionType.None) continue;
                if (obj.collider == null) continue;
                _spatialHash.Insert(obj);
            }

            // ── Narrow phase: check only spatially adjacent pairs ────────────────
            _spatialHash.QueryPairs((objA, objB) =>
            {
                if (objA is Bullet && objB is Bullet) return;  // skip bullet-bullet pairs entirely
                if ((objA.CollisionType & objB.CollisionType) != 0) return;
                if (objA.CheckCollision(objB))
                {
                    objA.OnCollision(objB);
                    objB.OnCollision(objB);
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
                {
                    if (bullet.IsActive)
                        _bulletSpatialHash.Insert(bullet);
                }
            }

            // Rebuild the ship spatial hash once per frame so that every ship
            // can do a cheap local query in FindNearestEnemy() instead of scanning
            // the full enemy list.
            _shipSpatialHash.Clear();
            if (_gameObjectsByType.TryGetValue(typeof(Ship), out List<GameObject> shipList))
            {
                foreach (GameObject ship in shipList)
                    _shipSpatialHash.Insert(ship);

                // ── Clump-leader election ─────────────────────────────────────
                // Run on the main thread (single-threaded, O(n)) only once every
                // ClumpElectionInterval frames.  Between elections the previous
                // assignments are reused — ships move slowly enough (~100 px/s,
                // ~1.7 px/frame) that clumps stay valid for many frames.
                //
                // Edge case: if a leader dies mid-interval its followers detect
                // this via ClumpLeader.IsActive in Ship.Update() and fall back to
                // computing everything themselves until the next election.
                if (--_clumpElectionCountdown <= 0)
                {
                    _clumpElectionCountdown = ClumpElectionInterval;
                    _clumpCellLeaders.Clear();
                    foreach (GameObject go in shipList)
                    {
                        Ship s = (Ship)go;
                        Point center = s.GetPosition().Center;
                        int cx = (int)Math.Floor((double)center.X / ClumpCellSize);
                        int cy = (int)Math.Floor((double)center.Y / ClumpCellSize);
                        int team = (int)(s.CollisionType & CollisionType.Teams);
                        long key = ((long)team << 60)
                                 | ((long)((uint)cx & 0x3FFFFFFF) << 30)
                                 | ((uint)cy & 0x3FFFFFFF);

                        if (_clumpCellLeaders.TryGetValue(key, out Ship leader))
                            s.ClumpLeader = leader;         // follower
                        else
                        {
                            _clumpCellLeaders[key] = s;
                            s.ClumpLeader = null;           // leader
                        }
                    }
                }
                // ─────────────────────────────────────────────────────────────

                // ── Phase 1: leader ships only ────────────────────────────────
                // Leaders call FindNearestEnemy + AvoidObstacles and publish
                // ClumpTarget / ClumpAvoidance before any follower reads them.
                Parallel.ForEach(shipList, go =>
                {
                    if (((Ship)go).ClumpLeader == null)
                        go.Update(gameTime);
                });

                // ── Phase 2: follower ships only ──────────────────────────────
                // All leaders have finished writing; followers can safely read
                // ClumpTarget and ClumpAvoidance with no locks.
                Parallel.ForEach(shipList, go =>
                {
                    if (((Ship)go).ClumpLeader != null)
                        go.Update(gameTime);
                });
            }

            // Update all non-ships sequentially (usually a tiny list)
            foreach (var (type, list) in _gameObjectsByType)
            {
                if (type == typeof(Ship)) continue;
                foreach (var go in list) go.Update(gameTime);
            }

            // Check Collission
            CheckCollision();

            while (_toBeAdded.TryDequeue(out GameObject gameObject))
            {
                gameObject.Load(_content);
                AddToTypeCache(gameObject);
            }

            while (_toBeRemoved.TryDequeue(out GameObject gameObject))
            {
                if (!gameObject.IsActive)
                {
                    gameObject.Destroy();
                    RemoveFromTypeCache(gameObject);
                }
            }
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            // ---------------------------------------------------------------
            // Viewport culling.
            //
            // Compute the visible region in WORLD space once per frame by
            // inverting WorldMatrix and transforming the four screen corners.
            // Every draw loop below skips objects whose bounding box does not
            // intersect this rectangle.
            // ---------------------------------------------------------------
            Viewport vp = GraphicsDevice.Viewport;
            Matrix invWorld = Matrix.Invert(WorldMatrix);

            Vector2 worldTL = Vector2.Transform(Vector2.Zero, invWorld);
            Vector2 worldBR = Vector2.Transform(new Vector2(vp.Width, vp.Height), invWorld);

            Rectangle worldView = new Rectangle(
                (int)Math.Min(worldTL.X, worldBR.X),
                (int)Math.Min(worldTL.Y, worldBR.Y),
                (int)Math.Abs(worldBR.X - worldTL.X),
                (int)Math.Abs(worldBR.Y - worldTL.Y));


            // ---------------------------------------------------------------
            // Pass 1: Ships
            // ---------------------------------------------------------------
            if (_gameObjectsByType.TryGetValue(typeof(Ship), out List<GameObject> ships) && ships.Count > 0)
            {
                Effect shader = ((Ship)ships[0]).ShaderEffect;

                // Compute projection once, identical for every ship in this frame.
                Matrix proj = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, -1);
                shader.Parameters["MatrixTransform"].SetValue(WorldMatrix * proj);

                // --- Team 1 pass ---
                // Find the first Team1 ship to get the team colour, then batch-draw all.
                Color team1 = FindTeamColor(ships, CollisionType.Team1);
                shader.Parameters["TeamColor"].SetValue(team1.ToVector4());
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend,
                                  null, null, null, shader);
                foreach (GameObject obj in ships)
                {
                    Ship s = (Ship)obj;
                    if ((s.CollisionType & CollisionType.Team1) != 0
                        && worldView.Intersects(s.GetPosition()))   // OPT-6
                        s.DrawBatched(gameTime, spriteBatch);
                }
                spriteBatch.End();


                // --- Team 2 pass ---
                Color team2 = FindTeamColor(ships, CollisionType.Team2);
                shader.Parameters["TeamColor"].SetValue(team2.ToVector4());
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend,
                                  null, null, null, shader);
                foreach (GameObject obj in ships)
                {
                    Ship s = (Ship)obj;
                    if ((s.CollisionType & CollisionType.Team2) != 0
                        && worldView.Intersects(s.GetPosition()))   // OPT-6
                        s.DrawBatched(gameTime, spriteBatch);
                }
                spriteBatch.End();

            }

            // ---------------------------------------------------------------
            // Pass 2: GPU particles: raw GraphicsDevice draw, no SpriteBatch.
            // Drawn here so we never need to End/Begin a SpriteBatch mid-loop.
            // ---------------------------------------------------------------
            if (_gameObjectsByType.TryGetValue(typeof(GpuParticleEmitter),
                    out List<GameObject> emitters))
            {
                foreach (GameObject obj in emitters)
                    ((GpuParticleEmitter)obj).DrawGpu(GraphicsDevice, WorldMatrix);
            }

            // ---------------------------------------------------------------
            // Pass 3: Everything else (bullets, etc.)
            // ---------------------------------------------------------------
            spriteBatch.Begin(transformMatrix: WorldMatrix);
            foreach (var bucket in _gameObjectsByType)
            {
                if (bucket.Key == typeof(Ship)) continue;
                if (bucket.Key == typeof(GpuParticleEmitter)) continue;

                foreach (GameObject go in bucket.Value)
                {
                    if (worldView.Intersects(go.GetPosition()))
                        go.Draw(gameTime, spriteBatch);
                }
            }
            spriteBatch.End();

            // ---------------------------------------------------------------
            // Pass 4: HUD (screen space).
            // ---------------------------------------------------------------
            spriteBatch.Begin();
            counter.Draw(gameTime, spriteBatch);
            spriteBatch.End();
        }

        private static Color FindTeamColor(List<GameObject> ships, CollisionType team)
        {
            foreach (GameObject obj in ships)
            {
                Ship s = (Ship)obj;
                if ((s.CollisionType & team) != 0) return s.TeamColorValue;
            }
            return Color.White;
        }


        /// <summary>
        /// Add a new GameObject to the GameManager. 
        /// The GameObject will be added at the start of the next Update step. 
        /// Once it is added, the GameManager will ensure all steps of the game loop will be called on the object automatically. 
        /// </summary>
        /// <param name="gameObject"> The GameObject to add. </param>
        public void AddGameObject(GameObject gameObject)
        {
            _toBeAdded.Enqueue(gameObject);
        }

        /// <summary>
        /// Remove GameObject from the GameManager. 
        /// The GameObject will be removed at the start of the next Update step and its Destroy() mehtod will be called.
        /// After that the object will no longer receive any updates.
        /// </summary>
        /// <param name="gameObject"> The GameObject to Remove. </param>
        public void RemoveGameObject(GameObject gameObject)
        {
            _toBeRemoved.Enqueue(gameObject);
        }

        private void AddToTypeCache(GameObject gameObject)
        {
            Type type = gameObject.GetType();
            if (!_gameObjectsByType.TryGetValue(type, out var list))
            {
                list = new List<GameObject>();
                _gameObjectsByType[type] = list;
            }

            gameObject.TypeCacheIndex = list.Count;
            list.Add(gameObject);

            gameObject.AllObjectsIndex = _allGameObjects.Count;
            gameObject.IsActive = true;

            _allGameObjects.Add(gameObject); // keep flat list in sync
        }

        private void RemoveFromTypeCache(GameObject go)
        {
            Type type = go.GetType();
            if (!_gameObjectsByType.TryGetValue(type, out var list)) return;

            int ti = go.TypeCacheIndex;
            int lastTi = list.Count - 1;

            // Guard: index must be valid AND must actually point to this object.
            // Stale indices happen with pool bullets that were never registered here.
            if (ti >= 0 && ti <= lastTi && list[ti] == go)
            {
                if (ti != lastTi)
                {
                    list[ti] = list[lastTi];
                    list[ti].TypeCacheIndex = ti;
                }
                list.RemoveAt(lastTi);
            }
            else
            {
                list.Remove(go);   // safe O(n) fallback, should almost never hit
            }

            go.TypeCacheIndex = -1;
            if (list.Count == 0) _gameObjectsByType.Remove(type);

            // ── Flat list ────────────────────────────────────────────────────────
            int ai = go.AllObjectsIndex;
            int lastAi = _allGameObjects.Count - 1;

            if (ai >= 0 && ai <= lastAi && _allGameObjects[ai] == go)
            {
                if (ai != lastAi)
                {
                    _allGameObjects[ai] = _allGameObjects[lastAi];
                    _allGameObjects[ai].AllObjectsIndex = ai;
                }
                _allGameObjects.RemoveAt(lastAi);
            }
            else
            {
                _allGameObjects.Remove(go);   // safe fallback
            }

            go.AllObjectsIndex = -1;
            go.IsActive = false;
        }

        public List<GameObject> GetGameObjects()
        {
            // Returns the maintained flat list - no allocation, no LINQ.
            return _allGameObjects;
        }

        public List<T> GetGameObjectsByType<T>() where T : GameObject
        {
            if (_gameObjectsByType.TryGetValue(typeof(T), out List<GameObject> typeObjects))
            {
                return typeObjects.Cast<T>().ToList();
            }

            return new List<T>();
        }

        public List<GameObject> GetRawList(Type type)
        {
            _gameObjectsByType.TryGetValue(type, out var list);
            return list;
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