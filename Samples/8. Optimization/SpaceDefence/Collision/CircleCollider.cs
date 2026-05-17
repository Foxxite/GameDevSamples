using System;
using SpaceDefence.Collision;
using Microsoft.Xna.Framework;

namespace SpaceDefence
{
	public class CircleCollider : Collider, IEquatable<CircleCollider>
	{
		public float X;
		public float Y;
		public Vector2 Center
		{
			get
			{
				return new Vector2(X, Y);
			}

			set
			{
				X = value.X;
				Y = value.Y;
			}
		}
		public float Radius;

		/// <summary>
		/// Creates a new Circle object.
		/// </summary>
		/// <param name="x">The X coordinate of the circle's center</param>
		/// <param name="y">The Y coordinate of the circle's center</param>
		/// <param name="radius">The radius of the circle</param>
		public CircleCollider(float x, float y, float radius)
		{
			X = x;
			Y = y;
			Radius = radius;
		}

		/// <summary>
		/// Creates a new Circle object.
		/// </summary>
		/// <param name="center">The coordinates of the circle's center</param>
		/// <param name="radius">The radius of the circle</param>
		public CircleCollider(Vector2 center, float radius)
		{
			Center = center;
			Radius = radius;
		}


		/// <summary>
		/// Gets whether or not the provided coordinates lie within the bounds of this Circle.
		/// </summary>
		/// <param name="coordinates">The coordinates to check.</param>
		/// <returns>true if the coordinates are within the circle.</returns>
		public override bool Contains(Vector2 coordinates)
		{
			return (Center - coordinates).Length() < Radius;
		}

		/// <summary>
		/// Gets whether or not the Circle intersects another Circle.
		/// </summary>
		/// <param name="other">The Circle to check for intersection.</param>
		/// <returns>true there is any overlap between the two Circles.</returns>
		public override bool Intersects(CircleCollider other)
		{
			return (Center - other.Center).Length() < Radius + other.Radius;
		}


		/// <summary>
		/// Gets whether or not the Circle intersects the Rectangle.
		/// </summary>
		/// <param name="other">The Rectangle to check for intersection.</param>
		/// <returns>true there is any overlap between the Circle and the Rectangle.</returns>
		public override bool Intersects(RectangleCollider other)
		{
			// Check if the center of the circle is within the vertical or horizontal bounds of the rectangle, and if the circle overlaps with the rectangle in that direction.
			bool withinVerticalBand = Center.Y < other.shape.Bottom && Center.Y > other.shape.Top;
			bool withinHorizontalBand = Center.X < other.shape.Right && Center.X > other.shape.Left;

			if (withinVerticalBand && Center.X + Radius > other.shape.Left && Center.X - Radius < other.shape.Right)
				return true;
			if (withinHorizontalBand && Center.Y + Radius > other.shape.Top && Center.Y - Radius < other.shape.Bottom)
				return true;

			// If the center of the circle is outside the bounds of the rectangle, check if any of the corners of the rectangle are within the circle.
			Vector2[] corners = {
				new(other.shape.Left, other.shape.Top),
				new(other.shape.Right, other.shape.Top),
				new(other.shape.Left, other.shape.Bottom),
				new(other.shape.Right, other.shape.Bottom)
			};

			foreach (Vector2 corner in corners)
			{
				float distanceX = corner.X - Center.X;
				float distanceY = corner.Y - Center.Y;
				if (distanceX * distanceX + distanceY * distanceY < Radius * Radius)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Gets whether or not the Circle intersects the Line
		/// </summary>
		/// <param name="other">The Line to check for intersection</param>
		/// <returns>true there is any overlap between the Circle and the Line.</returns>
		public override bool Intersects(LinePieceCollider other)
		{
			return other.Intersects(this);
		}

		/// <summary>
		/// Get the enclosing Rectangle that surrounds the Circle.
		/// </summary>
		/// <returns></returns>
		public override Rectangle GetBoundingBox()
		{
			return new Rectangle((int)(X - Radius), (int)(Y - Radius), (int)(2 * Radius), (int)(2 * Radius));
		}

		public bool Equals(CircleCollider other)
		{
			return other.X == X && other.Y == Y && other.Radius == Radius;
		}
	}
}
