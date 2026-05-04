using UnityEditor;
using UnityEngine;

namespace ProtoShape2D.Editor.Extensions
{
	public static class EditorPlaneExtension
	{
		public static Vector2 GetMousePosition(this Plane plane)
		{
			var mRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
			if (plane.Raycast(mRay, out var mRayDist))
			{
				return mRay.GetPoint(mRayDist);
			}
			return Vector2.zero;
		}
	}
}