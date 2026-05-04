using ProtoShape2D.Extensions;
using UnityEngine;

namespace ProtoShape2D
{
	[System.Serializable]
	public class PS2DPoint : object
	{
		public Vector2 position = Vector2.zero;
		public float curve = 0f;
		public string name;
		public bool selected = false;
		public bool clockwise = true;
		public Vector2 median = Vector2.zero;
		public Vector2 handleP = Vector2.zero;

		public Vector2 handleN = Vector2.zero;

		//IDs if point handle and bezier handles
		public int controlID = 0;
		public int controlPID = 0;
		public int controlNID = 0;
		public PS2DPointType pointType = PS2DPointType.None;

		public PS2DPoint(Vector2 position, string name = "")
		{
			this.position = position;
			this.name = name;
			this.handleP = position;
			this.handleN = position;
		}

		public void Move(Vector2 diff, bool moveHandles)
		{
			position += diff;
			//This is for when handles aren't calculated automatically
			if (moveHandles)
			{
				handleN += diff;
				handleP += diff;
			}
		}
		
		public void StraightenHandles()
		{
			var middle = ((handleP - position).normalized + (handleN - position).normalized).normalized;
			if (middle == Vector2.zero)
			{
				return;
			}
			var newHandleP = middle.Rotate(-90f);
			var newHandleN = middle.Rotate(+90f);
			if (Vector2.Distance(handleP, newHandleP + position) < Vector2.Distance(handleP, newHandleN + position))
			{
				handleP = (newHandleP * (handleP - position).magnitude) + position;
				handleN = (newHandleN * (handleN - position).magnitude) + position;
			}
			else
			{
				handleP = (newHandleN * (handleP - position).magnitude) + position;
				handleN = (newHandleP * (handleN - position).magnitude) + position;
			}
		}
	}
}