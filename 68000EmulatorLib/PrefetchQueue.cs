namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// CPU prefetch queue for the Motorola 68000 processor.
    /// </summary>
    public sealed class PrefetchQueue : Queue<ushort>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrefetchQueue"/> class with a default capacity of 2.
        /// </summary>
        /// <remarks>This constructor creates a prefetch queue with an initial capacity of 2.  Use this
        /// constructor when you want to initialize the queue with the default capacity.</remarks>
        public PrefetchQueue() : this(2)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefetchQueue"/> class with specified capacity.
        /// </summary>
        /// <param name="capacity">Maximum number of words the queue can hold (must be at least 1).</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if capacity is less than 1.</exception>
        private PrefetchQueue(int capacity) : base(capacity)
        {
        }

        /// <summary>
        /// Clone 
        /// </summary>
        /// <returns>New prefetch queue that is a copy of the original.</returns>
        public PrefetchQueue Clone()
        {
            PrefetchQueue clone = new(Capacity);
            clone.From(this);
            return clone;
        }

        /// <summary>
        /// Copies the contents and state from another prefetch queue into this instance.
        /// </summary>
        /// <param name="source">The source prefetch queue to copy from.</param>
        /// <exception cref="ArgumentNullException">Thrown if source is null.</exception>
        /// <exception cref="ArgumentException">Thrown if source has a different capacity.</exception>
        public void From(PrefetchQueue source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source == this)
                return;
            Clear();
            EnsureCapacity(source.Capacity);
            foreach(var item in source)
            {
                Enqueue(item);
            }
        }

        /// <summary>
        /// Number of bytes currently in the prefetch queue.
        /// </summary>
        public uint Size => (uint)(Count * sizeof(ushort));

        /// <summary>
        /// Gets a value indicating whether the queue is empty.
        /// </summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Gets a value indicating whether the queue is at capacity.
        /// </summary>
        public bool IsFull => Count == Capacity;
    }
}