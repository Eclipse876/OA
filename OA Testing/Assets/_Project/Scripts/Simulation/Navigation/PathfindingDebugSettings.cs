using UnityEngine;

namespace OA.Simulation.Navigation
{

    [CreateAssetMenu(
        fileName = "PathfindingDebugSettings",
        menuName = "OA/Navigation/Pathfinding Debug Settings")]
    public sealed class PathfindingDebugSettings : ScriptableObject
    {
        [Header("Runtime Debug Controls")]
        public bool enableRuntimeReroll = false;
        public bool enableRerollHotkey = true;

        [Header("Reroll Inputs")]
        [Min(0)] public int fixedSeed = 1;
        [Range(0.05f, 0.45f)] public float obstacleChance = 0.2f;
        [Range(0f, 0.85f)] public float roughWaterChance = 0f;
        [Range(0, 8)] public int smoothingPasses = 3;
    }
}