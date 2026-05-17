using System;
using SpaceDefence.Collision;
using Microsoft.Xna.Framework;

namespace SpaceDefence
{

	public class LinePieceCollider : Collider, IEquatable<LinePieceCollider>
	{

		public Vector2 Start;
		public Vector2 End;

		/// <summary>
		/// The length of the LinePiece, changing the length moves the end vector to adjust the length.
		/// </summary>
		public float Length
		{
			get
			{
				return (End - Start).Length();
			}
			set
			{
				End = Start + GetDirection() * value;
			}
		}

		/// <summary>
		/// The A component from the standard line formula Ax + By + C = 0
		/// </summary>
		public float StandardA
		{
			get
			{
				return Start.Y - End.Y;
			}
		}

		/// <summary>
		/// The B component from the standard line formula Ax + By + C = 0
		/// </summary>
		public float StandardB
		{
			get
			{
				return End.X - Start.X;
			}
		}

		/// <summary>
		/// The C component from the standard line formula Ax + By + C = 0
		/// </summary>
		public float StandardC
		{
			get
			{
				return Start.X * End.Y - End.X * Start.Y;
			}
		}

		public LinePieceCollider(Vector2 start, Vector2 end)
		{
			Start = start;
			End = end;
		}

		public LinePieceCollider(Vector2 start, Vector2 direction, float length)
		{
			Start = start;
			End = start + direction * length;
		}

		/// <summary>
		/// Should return the angle between a given direction and the up vector.
		/// </summary>
		/// <param name="direction">The Vector2 pointing out from (0,0) to calculate the angle to.</param>
		/// <returns> The angle in radians between the the up vector and the direction to the cursor.</returns>
		public static float GetAngle(Vector2 direction)
		{
			return (float)Math.Atan2(direction.X, -direction.Y);
		}


		/// <summary>
		/// Calculates the normalized vector pointing from point1 to point2
		/// </summary>
		/// <returns> A Vector2 containing the direction from point1 to point2. </returns>
		public static Vector2 GetDirection(Vector2 point1, Vector2 point2)
		{
			Vector2 direction = point2 - point1;
			direction.Normalize();
			return direction;
		}


		/// <summary>
		/// Gets whether or not the Line intersects another Line
		/// </summary>
		/// <param name="other">The Line to check for intersection</param>
		/// <returns>true there is any overlap between the Circle and the Line.</returns>
		public override bool Intersects(LinePieceCollider other)
		{
			float denominator = StandardA * other.StandardB - other.StandardA * StandardB;

			// A denominator of 0 means the lines are parallel, no intersection possible.
			if (denominator == 0)
				return false;

			// Compute the intersection point using the formula derived from Ax + By + C = 0.
			Vector2 intersection = new Vector2(
				(StandardB * other.StandardC - other.StandardB * StandardC) / denominator,
				(StandardC * other.StandardA - other.StandardC * StandardA) / denominator
			);

			// For line segments the intersection point must lie within both segments.
			return IsPointOnSegment(intersection) && other.IsPointOnSegment(intersection);
		}


		/// <summary>
		/// Gets whether or not the line intersects a Circle.
		/// </summary>
		/// <param name="other">The Circle to check for intersection.</param>
		/// <returns>true there is any overlap between the two Circles.</returns>
		public override bool Intersects(CircleCollider other)
		{
			Vector2 nearest = NearestPointOnLine(other.Center);
			float distanceSq = Vector2.DistanceSquared(nearest, other.Center);

			return distanceSq <= other.Radius * other.Radius;
		}

		/// <summary>
		/// Gets whether or not the Line intersects the Rectangle.
		/// </summary>
		/// <param name="other">The Rectangle to check for intersection.</param>
		/// <returns>true there is any overlap between the Circle and the Rectangle.</returns>
		public override bool Intersects(RectangleCollider other)
		{
			float topLeftSide = EvaluateLineEquation(new Vector2(other.shape.Left, other.shape.Top));
			float topRightSide = EvaluateLineEquation(new Vector2(other.shape.Right, other.shape.Top));
			float bottomLeftSide = EvaluateLineEquation(new Vector2(other.shape.Left, other.shape.Bottom));
			float bottomRightSide = EvaluateLineEquation(new Vector2(other.shape.Right, other.shape.Bottom));

			bool allOnPositiveSide = topLeftSide > 0 && topRightSide > 0 && bottomLeftSide > 0 && bottomRightSide > 0;
			bool allOnNegativeSide = topLeftSide < 0 && topRightSide < 0 && bottomLeftSide < 0 && bottomRightSide < 0;

			if (allOnPositiveSide || allOnNegativeSide)
				return false;

			return GetBoundingBox().Intersects(other.shape);
		}

		/// <summary>
		/// Calculates the intersection point between 2 lines.
		/// </summary>
		/// <param name="Other">The line to intersect with</param>
		/// <returns>A Vector2 with the point of intersection.</returns>
		public Vector2 GetIntersection(LinePieceCollider Other)
		{
			float denominator = StandardA * Other.StandardB - Other.StandardA * StandardB;

			// A denominator of 0 means the lines are parallel, no intersection possible.
			if (denominator == 0)
				return Vector2.Zero;

			return new Vector2(
				(StandardB * Other.StandardC - Other.StandardB * StandardC) / denominator,
				(StandardC * Other.StandardA - Other.StandardC * StandardA) / denominator
			);
		}

		/// <summary>
		/// Finds the nearest point on a line to a given vector, taking into account if the line is .
		/// </summary>
		/// <param name="other">The Vector you want to find the nearest point to.</param>
		/// <returns>The nearest point on the line.</returns>
		public Vector2 NearestPointOnLine(Vector2 other)
		{
			Vector2 lineDirection = End - Start;
			float lengthSq = lineDirection.LengthSquared();

			if (lengthSq == 0f)
				return Start;

			// Project 'other' onto the line, then clamp t to [0, 1] to stay within the segment.
			float t = Vector2.Dot(other - Start, lineDirection) / lengthSq;
			t = MathHelper.Clamp(t, 0f, 1f);

			return Start + t * lineDirection;
		}

		/// <summary>
		/// Returns the enclosing Axis Aligned Bounding Box containing the control points for the line.
		/// As an unbound line has infinite length, the returned bounding box assumes the line to be bound.
		/// </summary>
		/// <returns></returns>
		public override Rectangle GetBoundingBox()
		{
			Point topLeft = new Point((int)Math.Min(Start.X, End.X), (int)Math.Min(Start.Y, End.Y));
			Point size = new Point((int)Math.Max(Start.X, End.X), (int)Math.Max(Start.Y, End.Y)) - topLeft;

			return new Rectangle(topLeft, size);
		}


		/// <summary>
		/// Gets whether or not the provided coordinates lie on the line.
		/// </summary>
		/// <param name="coordinates">The coordinates to check.</param>
		/// <returns>true if the coordinates are within the circle.</returns>
		public override bool Contains(Vector2 coordinates)
		{
			const float epsilon = 0.0001f;

			Vector2 line = End - Start;
			Vector2 toPoint = coordinates - Start;

			// A non-zero cross product means the point is not collinear with the segment.
			float cross = line.X * toPoint.Y - line.Y * toPoint.X;
			if (Math.Abs(cross) > epsilon)
				return false;

			// A negative dot product means the point is behind Start;
			// a dot product exceeding the segment's squared length means it is past End.
			float dot = Vector2.Dot(toPoint, line);

			return dot >= 0 && dot <= line.LengthSquared();
		}

		public bool Equals(LinePieceCollider other)
		{
			return other.Start == Start && other.End == End;
		}

		/// <summary>
		/// Calculates the normalized vector pointing from point1 to point2
		/// </summary>
		/// <returns> A Vector2 containing the direction from point1 to point2. </returns>
		public static Vector2 GetDirection(Point point1, Point point2)
		{
			return GetDirection(point1.ToVector2(), point2.ToVector2());
		}


		/// <summary>
		/// Calculates the normalized vector pointing from point1 to point2
		/// </summary>
		/// <returns> A Vector2 containing the direction from point1 to point2. </returns>
		public Vector2 GetDirection()
		{
			return GetDirection(Start, End);
		}


		/// <summary>
		/// Should return the angle between a given direction and the up vector.
		/// </summary>
		/// <param name="direction">The Vector2 pointing out from (0,0) to calculate the angle to.</param>
		/// <returns> The angle in radians between the the up vector and the direction to the cursor.</returns>
		public float GetAngle()
		{
			return GetAngle(GetDirection());
		}

		/// <summary>
		/// Evaluates the implicit line equation Ax + By + C = 0 for a given point.
		/// A result of 0 means the point lies on the line.
		/// A positive result means the point is on the left side, negative means the right side.
		/// </summary>
		/// <param name="point">The point to evaluate.</param>
		/// <returns>The signed result of the line equation for the given point.</returns>
		private float EvaluateLineEquation(Vector2 point)
		{
			return StandardA * point.X + StandardB * point.Y + StandardC;
		}

		/// <summary>
		/// Checks whether a given point lies within this line segment.
		/// Uses the distance check: ||Start - point|| &lt;= Length and ||End - point|| &lt;= Length.
		/// </summary>
		/// <param name="point">The point to test, assumed to already lie on the infinite line through Start and End.</param>
		/// <returns>true if the point lies between Start and End.</returns>
		private bool IsPointOnSegment(Vector2 point)
		{
			float segmentLength = Length;
			return Vector2.Distance(Start, point) <= segmentLength &&
				   Vector2.Distance(End, point) <= segmentLength;
		}
	}
}
