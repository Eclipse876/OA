using UnityEngine;

namespace OA.Simulation.Units
{
    [CreateAssetMenu(
        fileName = "UnitArchetype", 
        menuName = "OA/Units/Unit Archetype")]

    public sealed class UnitArchetypeDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Unit";

        [Header("Profiles")]
        public MovementProfileDefinition movementProfile;
        public PresentationProfileDefinition presentationProfile;
    }

}        