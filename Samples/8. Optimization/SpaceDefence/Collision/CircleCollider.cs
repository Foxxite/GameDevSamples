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
			// Use squared distance to avoid the square root in Length()
			return (Center - coordinates).LengthSquared() < (Radius * Radius);
		}

		/// <summary>
		/// Gets whether or not the Circle intersects another Circle.
		/// </summary>
		/// <param name="other">The Circle to check for intersection.</param>
		/// <returns>true there is any overlap between the two Circles.</returns>
		public override bool Intersects(CircleCollider other)
		{
			// Compare squared distances to avoid computing square roots
			float radiusSum = Radius + other.Radius;
			return (Center - other.Center).LengthSquared() < (radiusSum * radiusSum);
		}


		/// <summary>
		/// Gets whether or not the Circle intersects the Rectangle.
		/// </summary>
		/// <param name="other">The Rectangle to check for intersection.</param>
		/// <returns>true there is any overlap between the Circle and the Rectangle.</returns>
		/// 
		/// Based on https://stackoverflow.com/a/402010
		public override bool Intersects(RectangleCollider other)
		{
			// Find the closest point to the circle within the rectangle
			float closestX = Math.Clamp(Center.X, other.shape.Left, other.shape.Right);
			float closestY = Math.Clamp(Center.Y, other.shape.Top, other.shape.Bottom);

			// Calculate the distance between the circle's center and this closest point
			float distanceX = Center.X - closestX;
			float distanceY = Center.Y - closestY;

			// If the distance is less than the circle's radius, an intersection occurs
			float distanceSquared = (distanceX * distanceX) + (distanceY * distanceY);
			return distanceSquared < (Radius * Radius);
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
