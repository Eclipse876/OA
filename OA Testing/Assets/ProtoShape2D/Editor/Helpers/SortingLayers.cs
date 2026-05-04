using System;
using System.Reflection;
using UnityEditorInternal;

namespace ProtoShape2D.Editor.Helpers
{
	public static class SortingLayers
	{
		//Get the sorting layer IDs
		public static int[] GetSortingLayerUniqueIDs()
		{
			var internalEditorUtilityType = typeof(InternalEditorUtility);
			var sortingLayerUniqueIDsProperty = internalEditorUtilityType.GetProperty("sortingLayerUniqueIDs", BindingFlags.Static | BindingFlags.NonPublic);
			return (int[])sortingLayerUniqueIDsProperty.GetValue(null, Array.Empty<object>());
		}

		//Get the sorting layer names
		public static string[] GetSortingLayerNames()
		{
			var internalEditorUtilityType = typeof(InternalEditorUtility);
			var sortingLayersProperty =
				internalEditorUtilityType.GetProperty("sortingLayerNames", BindingFlags.Static | BindingFlags.NonPublic);
			return (string[])sortingLayersProperty.GetValue(null, Array.Empty<object>());
		}
	}
}