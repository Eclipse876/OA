// PathfindingDebugSettings.cs:
// Inspector cheat panel for rerolling path maps while testing. Basically the
// "what if the ocean was terrible in a new way" button settings.
using UnityEngine;

namespace OA.Simulation.Navigation
{

    // ScriptableObject bag of debug knobs used by the sandbox controller.
    [CreateAssetMenu(
        fileName = "PathfindingDebugSettings",
        menuName = "OA/Navigation/Pathfinding Debug Settings")]
    public sealed class PathfindingDebugSettings : ScriptableObject
    {
        // Runtime switches for whether the sandbox is allowed to reroll maps on demand.
        [Header("Runtime Debug Controls")]
        public bool enableRuntimeReroll = false;
        public bool enableRerollHotkey = true;

        // Generation inputs used when the debug reroll button/hotkey fires.
        [Header("Reroll Inputs")]
        [Min(0)] public int fixedSeed = 1;
        [Range(0.05f, 0.45f)] public float obstacleChance = 0.2f;
        [Range(0f, 0.85f)] public float roughWaterChance = 0f;
        [Range(0, 8)] public int smoothingPasses = 3;
    }
}
