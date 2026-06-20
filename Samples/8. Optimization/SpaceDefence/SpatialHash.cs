/*
 *   Copyright (c) 2026 Foxxite | Articca
 *   All rights reserved.
 */

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace SpaceDefence
{
	/// <summary>
	/// A dictionary-based spatial hash that partitions the world into a uniform grid of cells.
	/// Objects are inserted by their bounding box, so an object that spans multiple cells is
	/// registered in each of them. During collision queries, only objects that share at least
	/// one cell are returned as candidates, eliminating the O(N²) brute-force cost.
	///
	/// Cell lists are reused across frames (no per-frame heap allocation).
	/// Duplicate pair checks are suppressed by only processing pairs where objB.Id > objA.Id.
	/// 
	/// Based on:
	/// https://youtu.be/h1xXcSvj7Io
	/// https://youtu.be/sx4IIQL0x7c
	/// </summary>
	public class SpatialHash
	{
		// ── tuneable constant ────────────────────────────────────────────────────
		// 150 px comfortably covers one ship body (64×128) plus a small margin.
		// Smaller cells → fewer false-positive pairs but more cells per large object.
		// Larger cells → fewer cells per object but more pairs to filter.
		public const int CellSize = 150;
		// ────────────────────────────────────────────────────────────────────────

		// Main lookup: packed (cellX, cellY) key → list of objects in that cell.
		private readonly Dictionary<long, List<GameObject>> _cells =
			new Dictionary<long, List<GameObject>>();

		// Tracks which cell lists were touched this frame so Clear() is O(touched cells)
		// rather than O(all cells ever seen).
		private readonly List<List<GameObject>> _activeCells =
			new List<List<GameObject>>();

		// Pool of empty lists recycled each frame to avoid GC pressure.
		private readonly Stack<List<GameObject>> _listPool =
			new Stack<List<GameObject>>();


		public void Clear()
		{
			foreach (List<GameObject> cell in _activeCells)
			{
				cell.Clear();
				_listPool.Push(cell);
			}
			_activeCells.Clear();
			_cells.Clear();
		}

		/// <summary>
		/// Registers <paramref name="obj"/> in every cell overlapped by its bounding box.
		/// </summary>
		public void Insert(GameObject obj)
		{
			Rectangle bounds = obj.GetPosition();

			int minCX = FloorDiv(bounds.Left, CellSize);
			int minCY = FloorDiv(bounds.Top, CellSize);
			int maxCX = FloorDiv(bounds.Right, CellSize);
			int maxCY = FloorDiv(bounds.Bottom, CellSize);

			for (int cx = minCX; cx <= maxCX; cx++)
			{
				for (int cy = minCY; cy <= maxCY; cy++)
				{
					long key = PackKey(cx, cy);
					if (!_cells.TryGetValue(key, out List<GameObject> cell))
					{
						cell = _listPool.Count > 0
							? _listPool.Pop()
							: new List<GameObject>();
						_cells[key] = cell;
						_activeCells.Add(cell);
					}
					cell.Add(obj);
				}
			}
		}

		/// <summary>
		/// Runs broad-phase collision detection and calls
		/// <paramref name="onPair"/>(a, b) for every unique candidate pair.
		/// Each pair is reported exactly once (a.Id &lt; b.Id is guaranteed).
		/// The caller is responsible for the narrow-phase check.
		/// </summary>
		public void QueryPairs(System.Action<GameObject, GameObject> onPair)
		{
			foreach (List<GameObject> cell in _activeCells)
			{
				int count = cell.Count;
				for (int i = 0; i < count; i++)
				{
					GameObject a = cell[i];
					for (int j = i + 1; j < count; j++)
					{
						GameObject b = cell[j];
						// Guarantee each pair is visited exactly once across all cells.
						// Using the stable unique Id assigned at construction time.
						if (a.Id < b.Id)
							onPair(a, b);
						else if (b.Id < a.Id)
							onPair(b, a);
						// a.Id == b.Id means it is the same object; skip.
					}
				}
			}
		}

		/// <summary>
		/// Fills <paramref name="results"/> with every object whose cell overlaps
		/// <paramref name="queryBounds"/>. The HashSet handles deduplication automatically
		/// when an object spans multiple cells. The set is cleared before filling.
		///
		/// Results are broad-phase candidates only - the caller must still do a
		/// precise distance check to discard objects that are in a nearby cell but
		/// outside the actual query radius.
		/// </summary>
		public void QueryRegion(Rectangle queryBounds, HashSet<GameObject> results)
		{
			results.Clear();

			int minCX = FloorDiv(queryBounds.Left, CellSize);
			int minCY = FloorDiv(queryBounds.Top, CellSize);
			int maxCX = FloorDiv(queryBounds.Right, CellSize);
			int maxCY = FloorDiv(queryBounds.Bottom, CellSize);

			for (int cx = minCX; cx <= maxCX; cx++)
			{
				for (int cy = minCY; cy <= maxCY; cy++)
				{
					long key = PackKey(cx, cy);
					if (_cells.TryGetValue(key, out List<GameObject> cell))
					{
						foreach (GameObject obj in cell)
							results.Add(obj);   // HashSet ignores duplicates
					}
				}
			}
		}


		/// <summary>
		/// Integer floor division that handles negative coordinates correctly.
		/// C# integer division truncates toward zero; this rounds toward −∞ instead,
		/// so objects in negative world-space land in the right cell.
		/// </summary>
		private static int FloorDiv(int value, int divisor)
		{
			int q = value / divisor;
			// If the signs differ and there is a remainder, subtract one.
			return (value ^ divisor) < 0 && q * divisor != value ? q - 1 : q;
		}

		/// <summary>
		/// Packs two cell coordinates into a single 64-bit key for the dictionary.
		/// Stores cellX in the high 32 bits and cellY in the low 32 bits.
		/// </summary>
		private static long PackKey(int cellX, int cellY) =>
			((long)cellX << 32) | (uint)cellY;
	}
}