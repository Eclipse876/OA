using System.Collections.Generic;

namespace OA.Simulation.Orders
{
    public sealed class OrderQueue
    {
        private readonly Queue<Order> _orders = new Queue<Order>();

        public int Count => _orders.Count;
        public bool HasOrders => _orders.Count > 0;

        public void Enqueue(Order order)
        {
            if (order == null)
                return;
            
            _orders.Enqueue(order);
        }

        public bool TryPeak(out Order order)
        {
            if (_orders.Count > 0)
            {
                order = _orders.Peek();
                return true;
            }

            order = null;
            return false;
        }

        public void Clear()
        {
            _orders.Clear();
        }

    }

}