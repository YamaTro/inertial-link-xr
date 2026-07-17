namespace YamaTro.InertialLink.Core
{
    /// <summary>
    /// Allocates each u32 value at most once and permanently fails after the last value.
    /// A caller must establish a new replay scope before creating a new counter.
    /// </summary>
    public sealed class NonWrappingSequenceCounter
    {
        private uint next;

        public NonWrappingSequenceCounter(uint firstSequence)
        {
            next = firstSequence;
        }

        public NonWrappingSequenceCounter() : this(1U) { }

        public bool IsExhausted { get; private set; }

        public bool TryTake(out uint sequence)
        {
            if (IsExhausted)
            {
                sequence = 0;
                return false;
            }

            sequence = next;
            if (next == uint.MaxValue)
            {
                IsExhausted = true;
            }
            else
            {
                next++;
            }
            return true;
        }
    }
}
