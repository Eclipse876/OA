using UnityEngine;

namespace ProtoShape2D.Editor.Helpers
{
	public class EditorGeometry
	{
		//This gets position of that green cursor for creating points
		public static Vector2 GetBasePoint(Vector2 b1, Vector2 b2, Vector2 t, float sizeCap = 0f)
		{
			var d1 = Vector2.Distance(b1, t);
			var d2 = Vector2.Distance(b2, t);
			var db = Vector2.Distance(b1, b2);
			//Find one of the angles
			var angle1 = Mathf.Acos((Mathf.Pow(d1, 2) + Mathf.Pow(db, 2) - Mathf.Pow(d2, 2)) / (2 * d1 * db));
			//Find distance to point
			var dist = Mathf.Cos(angle1) * d1;
			//Make sure it's within the line
			if (dist < sizeCap || dist > db - sizeCap) return Vector2.zero;
			else return (b1 + (dist * (b2 - b1).normalized));
		}

		//Find a point on an infinite line. Same as above but with infinite line
		public static Vector2 NearestPointOnLine(Vector2 lineStart, Vector2 lineDirection, Vector2 point)
		{
			lineDirection.Normalize();
			return lineStart + lineDirection * Vector2.Dot(lineDirection, point - lineStart);
		}
		
	}
}