using UnityEngine;

namespace ProtoShape2D.Helpers
{
	public static class Geometry
	{
		public static Vector2 LineIntersectionPoint(Vector2 l1s, Vector2 l1e, Vector2 l2s, Vector2 l2e)
		{
			//Get A,B,C of first line
			var A1 = l1e.y - l1s.y;
			var B1 = l1s.x - l1e.x;
			var C1 = A1 * l1s.x + B1 * l1s.y;
			//Get A,B,C of second line
			var A2 = l2e.y - l2s.y;
			var B2 = l2s.x - l2e.x;
			var C2 = A2 * l2s.x + B2 * l2s.y;
			//Get delta and check if the lines are parallel
			var delta = A1 * B2 - A2 * B1;
			//if(delta==0) throw new System.Exception("Lines are parallel");
			//Special case where the angle is too small
			if (delta < 0.01f && delta > -0.01f && l1e == l2s) return l1e;
			// now return the Vector2 intersection point
			return new Vector2(
				(B2 * C1 - B1 * C2) / delta,
				(A1 * C2 - A2 * C1) / delta
			);
		}
	}
}