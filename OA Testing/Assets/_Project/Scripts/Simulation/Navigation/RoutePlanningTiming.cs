using UnityEngine;

namespace OA.Simulation.Navigation
{
    // Recent successful attempt time per unit of route work. A retry's reserved
    // lead is deliberately not a sample: it includes unused future waiting time.
    public sealed class RoutePlanningTiming
    {
        private readonly float[] secondsPerWorkStep = new float[24];

        public float EstimateLead(
            int estimatedSteps,
            int waypointCount,
            float stepsPerSecond,
            float minimumLead,
            float safetySeconds,
            float maximumLead,
            bool requiresCorridorSearch = false)
        {
            float work = WorkSteps(estimatedSteps, waypointCount);
            float rate = secondsPerWorkStep[Bucket(estimatedSteps, waypointCount, requiresCorridorSearch)];
            if (rate <= 0f) rate = 1f / Mathf.Max(1000f, stepsPerSecond);
            return Mathf.Clamp(
                Mathf.Max(minimumLead, work * rate + safetySeconds),
                0.05f,
                Mathf.Max(0.1f, maximumLead));
        }

        public void RecordSuccess(
            int estimatedSteps,
            int waypointCount,
            float elapsedSeconds,
            bool requiresCorridorSearch = false)
        {
            if (elapsedSeconds <= 0f || float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds))
            {
                return;
            }

            int bucket = Bucket(estimatedSteps, waypointCount, requiresCorridorSearch);
            float sample = elapsedSeconds / WorkSteps(estimatedSteps, waypointCount);
            float previous = secondsPerWorkStep[bucket];
            secondsPerWorkStep[bucket] = previous > 0f
                ? Mathf.Lerp(previous, sample, 0.65f)
                : sample;
        }

        public void Clear()
        {
            System.Array.Clear(secondsPerWorkStep, 0, secondsPerWorkStep.Length);
        }

        private static float WorkSteps(int estimatedSteps, int waypointCount)
        {
            return Mathf.Max(1, estimatedSteps) *
                (1f + Mathf.Min(16, Mathf.Max(0, waypointCount - 1)) * 0.12f);
        }

        private static int Bucket(int steps, int waypoints, bool requiresCorridorSearch)
        {
            int distanceBucket = steps < 8000 ? 0 : steps < 20000 ? 1 :
                steps < 40000 ? 2 : 3;
            int queueBucket = waypoints <= 1 ? 0 : waypoints <= 3 ? 1 : 2;
            return (requiresCorridorSearch ? 12 : 0) + queueBucket * 4 + distanceBucket;
        }
    }
}
