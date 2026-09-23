using UnityEngine;

namespace OA.Simulation.Units
{
    [CreateAssetMenu(fileName = "PresentationProfile", menuName = "OA/Units/Presentation Profile")]
    public sealed class PresentationProfileDefinition : ScriptableObject
    {
        public Color hullColor = new Color(0.77f, 0.89f, 1f);
    }
}
