using UnityEngine;

namespace OA.Presentation.Units
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipNavigationAgent))]
    public sealed class SelectableShip : MonoBehaviour
    {
        [SerializeField] private LineRenderer selectionRing;

        private const int RingSegments = 48;

        public ShipNavigationAgent Agent { get; private set; }
        public bool IsSelected { get; private set; }

        private void Awake()
        {
            Agent = GetComponent<ShipNavigationAgent>();
            var archetype = Agent.Archetype;

            if (selectionRing == null ||
                archetype == null ||
                archetype.movementProfile == null)
            {
                UnityEngine.Debug.LogError(
                    $"{name}: Assign an archetype, movement profile, and selection ring.",
                    this);
                return;
            }

            float radius =
                archetype.movementProfile.HalfLengthWorldUnits + 1.5f;

            if (archetype.presentationProfile != null)
            {
                radius *= archetype.presentationProfile.selectionRingScale;
            }

            BuildRing(radius);
            SetSelected(false);
        }

        private void BuildRing(float radius)
        {
            selectionRing.useWorldSpace = false;
            selectionRing.loop = true;
            selectionRing.positionCount = RingSegments;
            selectionRing.startWidth = 0.35f;
            selectionRing.endWidth = 0.35f;
            selectionRing.startColor = Color.green;
            selectionRing.endColor = Color.green;

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;

                selectionRing.SetPosition(
                    i,
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f));
            }
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            if (selectionRing != null)
            {
                selectionRing.enabled = selected;
            }
        }
    }
}