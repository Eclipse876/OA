using System.Drawing;
using UnityEngine;

namespace OA.Simulation.Units
{
    [CreateAssetMenu(
        fileName = "PresentationProfile",
        menuName = "OA/Units/Presentation Profile")]
    public sealed class PresentationProfileDefinition : ScriptableObject
    {
        [Header("Sprites")]
        public Sprite worldSprite;
        public Sprite mapIconSprite;

        [Header("Selection")]
        [Min(0.1f)] public float selectionRingScale = 1f;

        [Header("Path")]
        [Min(0.1f)] public float pathLineWidth = 0.1f;

        [Header("Tint")]
        public Color tint = Color.white;
    }

}