using UnityEngine;

namespace OA.Simulation.Units
{
    [CreateAssetMenu(fileName = "UnitArchetype", menuName = "OA/Units/Unit Archetype")]
    public sealed class UnitArchetypeDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Basic Ship";

        [Header("Profiles")]
        public MovementProfileDefinition movementProfile;
        public PresentationProfileDefinition presentationProfile;
    }
}
