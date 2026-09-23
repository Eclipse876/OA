using UnityEngine;

namespace OA.LegacyTechDemo
{
    public static class LegacyPathfindingDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<LegacyPathfindingDemoController>() != null)
            {
                return;
            }

            var root = new GameObject("OA_LegacyPathfindingTechDemo");
            root.AddComponent<LegacyPathfindingDemoController>();
        }
    }
}
