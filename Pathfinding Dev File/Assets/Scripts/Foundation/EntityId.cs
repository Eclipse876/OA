namespace OA.Foundation
{
    public readonly struct EntityId
    {
        private static int s_nextValue;

        public int Value { get; }

        private EntityId(int value)
        {
            Value = value;
        }

        public static EntityId Create()
        {
            return new EntityId(++s_nextValue);
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
