/*
 *   Copyright (c) 2026 Foxxite | Articca
 *   All rights reserved.
 */

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace SpaceDefence
{
	/// <summary>
	/// Flat-array spatial hash.
	///
	/// Based on the following:
	/// https://gist.github.com/sixman9/805806
	/// https://github.com/benjitrosch/spatial-hash/blob/main/SpatialHash.cs
	/// https://www.youtube.com/watch?v=sx4IIQL0x7c
	/// https://www.youtube.com/watch?v=h1xXcSvj7Io
	/// https://gamedev.net/tutorials/programming/general-and-gameplay-programming/spatial-hashing-r2697/
	/// </summary>
	public class SpatialHash
	{
		// ── Configuration ────────────────────────────────────────────────────────

		private int _cellSize = 150;

		/// <summary>
		/// Width/height of each grid cell in world units.
		/// Changing this automatically rebuilds the flat grid.
		/// </summary>
		public int CellSize
		{
			get => _cellSize;
			set { _cellSize = value; Rebuild(); }
		}

		// World bounds: objects outside are silently ignored during Insert.
		// Defaults cover a 1920×1080 viewport plus margin.
		private int _worldMinX = -1000, _worldMinY = -1000;
		private int _worldMaxX = 4000, _worldMaxY = 4000;

		/// <summary>
		/// Override the world bounds and immediately rebuild the grid.
		/// </summary>
		public void SetWorldBounds(int minX, int minY, int maxX, int maxY)
		{
			_worldMinX = minX; _worldMinY = minY;
			_worldMaxX = maxX; _worldMaxY = maxY;
			Rebuild();
		}

		// ── Internal flat grid ────────────────────────────────────────────────────

		// 1-D array: _buckets[(cx + _offsetCX) * _gridH + (cy + _offsetCY)]
		// null  → cell is empty (no list allocated for it this frame).
		// !null → list of objects registered in this cell.
		private List<GameObject>[] _buckets = Array.Empty<List<GameObject>>();
		private int _gridW, _gridH;
		private int _offsetCX, _offsetCY;   // shift so negative cell coords map to index ≥ 0

		// Which flat indices are occupied this frame
		private readonly List<int> _activeIndices = new List<int>();

		// Recycled cell lists, avoids per-frame heap allocation.
		private readonly Stack<List<GameObject>> _listPool = new Stack<List<GameObject>>();

		// Deduplication for QueryPairs.
		private readonly HashSet<long> _seenPairs = new HashSet<long>();

		// ── Public API ───────────────────────────────────────────────────────────

		public bool IsEmpty => _activeIndices.Count == 0;

		public SpatialHash() => Rebuild();

		/// <summary>
		/// Rebuilds the flat grid using the current CellSize and world bounds.
		/// Called automatically by the CellSize property setter.
		/// </summary>
		public void Rebuild()
		{
			_offsetCX = -FloorDiv(_worldMinX, _cellSize);
			_offsetCY = -FloorDiv(_worldMinY, _cellSize);
			_gridW = FloorDiv(_worldMaxX, _cellSize) + _offsetCX + 2;
			_gridH = FloorDiv(_worldMaxY, _cellSize) + _offsetCY + 2;
			_buckets = new List<GameObject>[_gridW * _gridH];
			_activeIndices.Clear();
		}

		/// <summary>
		/// Clears all objects inserted this frame.
		/// </summary>
		public void Clear()
		{
			foreach (int idx in _activeIndices)
			{
				List<GameObject> cell = _buckets[idx];
				cell.Clear();
				_listPool.Push(cell);
				_buckets[idx] = null;
			}
			_activeIndices.Clear();
		}

		/// <summary>
		/// Registers <paramref name="obj"/> in every cell overlapped by its bounding box.
		/// Objects outside the declared world bounds are silently ignored.
		/// </summary>
		public void Insert(GameObject obj)
		{
			Rectangle b = obj.GetPosition();

			int minCX = FloorDiv(b.Left, _cellSize);
			int minCY = FloorDiv(b.Top, _cellSize);
			int maxCX = FloorDiv(b.Right, _cellSize);
			int maxCY = FloorDiv(b.Bottom, _cellSize);

			for (int cx = minCX; cx <= maxCX; cx++)
			{
				int ax = cx + _offsetCX;
				if ((uint)ax >= (uint)_gridW) continue;      // out of bounds - skip

				for (int cy = minCY; cy <= maxCY; cy++)
				{
					int ay = cy + _offsetCY;
					if ((uint)ay >= (uint)_gridH) continue;

					int idx = ax * _gridH + ay;
					if (_buckets[idx] == null)
					{
						_buckets[idx] = _listPool.Count > 0
							? _listPool.Pop()
							: new List<GameObject>();
						_activeIndices.Add(idx);
					}
					_buckets[idx].Add(obj);
				}
			}
		}

		/// <summary>
		/// Fills <paramref name="results"/> with every object whose cell overlaps
		/// <paramref name="queryBounds"/>.
		/// HashSet overload, deduplicates objects that span multiple cells.
		/// </summary>
		public void QueryRegion(Rectangle queryBounds, HashSet<GameObject> results)
		{
			results.Clear();

			int minCX = FloorDiv(queryBounds.Left, _cellSize);
			int minCY = FloorDiv(queryBounds.Top, _cellSize);
			int maxCX = FloorDiv(queryBounds.Right, _cellSize);
			int maxCY = FloorDiv(queryBounds.Bottom, _cellSize);

			for (int cx = minCX; cx <= maxCX; cx++)
			{
				int ax = cx + _offsetCX;
				if ((uint)ax >= (uint)_gridW) continue;

				for (int cy = minCY; cy <= maxCY; cy++)
				{
					int ay = cy + _offsetCY;
					if ((uint)ay >= (uint)_gridH) continue;

					List<GameObject> cell = _buckets[ax * _gridH + ay];
					if (cell == null) continue;

					foreach (GameObject obj in cell)
						results.Add(obj);
				}
			}
		}

		/// <summary>
		/// Fills <paramref name="results"/> with every object whose cell overlaps
		/// <paramref name="queryBounds"/>.
		/// List overload, may include duplicates for objects spanning multiple cells;
		/// acceptable when the caller already performs an exact distance check.
		/// </summary>
		public void QueryRegion(Rectangle queryBounds, List<GameObject> results)
		{
			results.Clear();

			int minCX = FloorDiv(queryBounds.Left, _cellSize);
			int minCY = FloorDiv(queryBounds.Top, _cellSize);
			int maxCX = FloorDiv(queryBounds.Right, _cellSize);
			int maxCY = FloorDiv(queryBounds.Bottom, _cellSize);

			for (int cx = minCX; cx <= maxCX; cx++)
			{
				int ax = cx + _offsetCX;
				if ((uint)ax >= (uint)_gridW) continue;

				for (int cy = minCY; cy <= maxCY; cy++)
				{
					int ay = cy + _offsetCY;
					if ((uint)ay >= (uint)_gridH) continue;

					List<GameObject> cell = _buckets[ax * _gridH + ay];
					if (cell == null) continue;

					results.AddRange(cell);
				}
			}
		}

		/// <summary>
		/// Calls <paramref name="onPair"/>(a, b) for every unique candidate collision
		/// pair where a.Id &lt; b.Id.  Each pair is reported exactly once even if the
		/// two objects share more than one cell.
		/// </summary>
		public void QueryPairs(Action<GameObject, GameObject> onPair)
		{
			_seenPairs.Clear();

			foreach (int idx in _activeIndices)
			{
				List<GameObject> cell = _buckets[idx];
				int count = cell.Count;

				for (int i = 0; i < count; i++)
				{
					GameObject a = cell[i];
					for (int j = i + 1; j < count; j++)
					{
						GameObject b = cell[j];

						int lo = Math.Min(a.Id, b.Id), hi = Math.Max(a.Id, b.Id);
						long pairKey = ((long)lo << 32) | (uint)hi;
						if (_seenPairs.Add(pairKey))
							onPair(a.Id < b.Id ? a : b, a.Id < b.Id ? b : a);
					}
				}
			}
		}

		// ── Internals ────────────────────────────────────────────────────────────

		/// <summary>
		/// Integer floor-division that handles negative coordinates correctly.
		/// C# truncates toward zero; this rounds toward −infinity.
		/// </summary>
		private static int FloorDiv(int value, int divisor)
		{
			int q = value / divisor;
			return (value ^ divisor) < 0 && q * divisor != value ? q - 1 : q;
		}
	}
}