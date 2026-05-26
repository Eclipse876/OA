using UnityEngine;

namespace OA.TechDemo
{
    public static class PathfindingDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<PathfindingDemoController>() != null)
            {
                return;
            }

            var root = new GameObject("OA_PathfindingTechDemo");
            root.AddComponent<PathfindingDemoController>();
        }
    }
}
