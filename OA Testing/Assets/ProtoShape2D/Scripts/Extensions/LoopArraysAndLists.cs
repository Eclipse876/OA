using System.Collections.Generic;
using UnityEngine;

namespace ProtoShape2D.Extensions
{
	public static class LoopArraysAndLists
	{
		public static int LoopID<T>(this List<T> list, int i)
		{
			if (i >= list.Count) return i % list.Count;
			return i < 0 ? (list.Count) * ((Mathf.Abs(i + 1) / list.Count) + 1) + i : i;
		}

		public static T Loop<T>(this List<T> list, int i)
		{
			if (i >= list.Count) return list[i % list.Count];
			return i < 0 ? list[(list.Count) * ((Mathf.Abs(i + 1) / list.Count) + 1) + i] : list[i];
		}

		public static int LoopID<T>(this T[] array, int i)
		{
			if (i >= array.Length) return i % array.Length;
			return i < 0 ? (array.Length) * ((Mathf.Abs(i + 1) / array.Length) + 1) + i : i;
		}

		public static T Loop<T>(this T[] array, int i)
		{
			if (i >= array.Length) return array[i % array.Length];
			return i < 0 ? array[(array.Length) * ((Mathf.Abs(i + 1) / array.Length) + 1) + i] : array[i];
		}
	}
}