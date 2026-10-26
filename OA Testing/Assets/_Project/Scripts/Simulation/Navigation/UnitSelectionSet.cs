using System.Collections.Generic;

namespace OA.Simulation.Navigation
{
    // Selection semantics are independent of input devices and scene lifetime.
    public sealed class UnitSelectionSet<T> where T : class
    {
        private readonly List<T> items = new List<T>();
        private readonly List<T> incoming = new List<T>();

        public IReadOnlyList<T> Items => items;
        public int Count => items.Count;

        public bool Contains(T unit) => unit != null && items.Contains(unit);

        public void Select(T unit, bool additive)
        {
            if (!additive) items.Clear();
            if (unit != null && !items.Contains(unit)) items.Add(unit);
        }

        public void Select(IEnumerable<T> units, bool additive)
        {
            // Snapshot first: a replacement may enumerate Items itself or a
            // filtered view of the current selection.
            incoming.Clear();
            if (units != null)
            {
                foreach (T unit in units)
                    if (unit != null && !incoming.Contains(unit)) incoming.Add(unit);
            }

            if (!additive) items.Clear();
            for (int i = 0; i < incoming.Count; i++)
                if (!items.Contains(incoming[i])) items.Add(incoming[i]);
            incoming.Clear();
        }

        public void Clear() => items.Clear();
        public bool Remove(T unit) => unit != null && items.Remove(unit);
    }
}
