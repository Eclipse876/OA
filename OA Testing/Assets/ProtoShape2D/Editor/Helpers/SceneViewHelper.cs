using UnityEditor;
using UnityEngine;

namespace ProtoShape2D.Editor.Helpers
{
	public static class SceneViewHelper
	{
		public static Vector3 GetCurrentSceneViewCenter()
		{
			var sc = SceneView.lastActiveSceneView != null
				? SceneView.lastActiveSceneView
				: SceneView.sceneViews[0] as SceneView;
			return sc.pivot;
		}
		
		public static float GetCurrentSceneViewSize()
		{
			var sc = SceneView.lastActiveSceneView != null
				? SceneView.lastActiveSceneView
				: SceneView.sceneViews[0] as SceneView;
			return sc.size;
		}
	}
}