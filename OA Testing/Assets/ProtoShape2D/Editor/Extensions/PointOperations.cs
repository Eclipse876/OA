using ProtoShape2D.Extensions;
using UnityEngine;

namespace ProtoShape2D.Editor.Extensions
{
	public static class PointOperations
	{
		public static void AddPoint(this ProtoShape2D ps2d, Vector2 pos, int after = -1)
		{
			if (after == -1 || after == ps2d.points.Count - 1)
			{
				after = ps2d.points.Count - 1;
				ps2d.points.Add(new PS2DPoint(pos));
			}
			else
			{
				ps2d.points.Insert(after + 1, new PS2DPoint(pos));
			}
			//Give all points new names
			for (var i = 0; i < ps2d.points.Count; i++)
			{
				ps2d.points[i].name = $"point{ps2d.uniqueName}_{i}";
			}
			//Set curve value based on neighboring points
			ps2d.points.Loop(after + 1).curve = Mathf.Lerp(ps2d.points.Loop(after).curve, 
				ps2d.points.Loop(after + 2).curve, 0.5f) * 0.5f;
		}
		public static void DeletePoint(this ProtoShape2D ps2d,int at)
		{
			ps2d.points.RemoveAt(at);
		}
		
		public static void SelectPoint(this ProtoShape2D ps2d, int i, bool state)
		{
			if (ps2d.points[i].selected == state)
			{
				return;
			}
			ps2d.points[i].selected = state;
		}
		
		public static void DeselectAllPoints(this ProtoShape2D ps2d)
		{
			foreach (var point in ps2d.points)
			{
				point.selected = false;
			}
		}
		
	}
}