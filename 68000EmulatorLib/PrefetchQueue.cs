using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// CPU prefetch queue for the Motorola 68000 processor.
    /// </summary>
    [Serializable]
    public sealed class PrefetchQueue : Queue<ushort>
    {
        private const int CAPACITY = 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefetchQueue"/> class with a default capacity of 2.
        /// </summary>
        public PrefetchQueue() : this(CAPACITY)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefetchQueue"/> class with specified capacity.
        /// </summary>
        /// <param name="capacity">Maximum number of words the queue can hold (must be at least 1).</param>
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
        public void From(PrefetchQueue source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source == this)
                return;
            Clear();
            // Queue doesn't expose capacity setter or Trim, but internal array grows effectively.
            // We rely on constructor or natural growth.
            foreach (var item in source)
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

        /// <summary>
        /// Helper property to expose expected capacity since Queue&lt;T&gt; does not expose it publicly.
        /// </summary>
        public new int Capacity 
        { 
            get => base.Capacity; 
            set 
            { 
                if (value != CAPACITY) { throw new ArgumentException("Capacity must be 2"); } 
            } 
        }
    }
}