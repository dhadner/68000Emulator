using System.Collections;
using System.Runtime.CompilerServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// CPU prefetch queue for the Motorola 68000 processor.
    /// </summary>
    public sealed class PrefetchQueue : IEnumerable<ushort>
    {
        private readonly ushort[] _buffer;
        private readonly int _capacity;
        private int _head;
        private int _count;

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
        private PrefetchQueue(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");

            _capacity = capacity;
            _buffer = new ushort[capacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Clone 
        /// </summary>
        /// <returns>New prefetch queue that is a copy of the original.</returns>
        public PrefetchQueue Clone()
        {
            PrefetchQueue clone = new(_capacity);
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
            if (source._capacity != _capacity)
                throw new ArgumentException("Source queue must have the same capacity.", nameof(source));
            _head = source._head;
            _count = source._count;
            Array.Copy(source._buffer, _buffer, _capacity);
        }

        /// <summary>
        /// Gets the number of words currently in the queue.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Number of bytes currently in the prefetch queue.
        /// </summary>
        public uint ByteCount => (uint)(_count * 2);

        /// <summary>
        /// Gets the maximum capacity of this queue in words.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets a value indicating whether the queue is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets a value indicating whether the queue is at capacity.
        /// </summary>
        public bool IsFull => _count == _capacity;

        /// <summary>
        /// Gets the word at the specified index without removing it from the queue.
        /// </summary>
        /// <param name="index">Zero-based index from the front of the queue.</param>
        /// <returns>The word at the specified index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if index is out of range.</exception>
        public ushort this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
                return _buffer[(_head + index) % _capacity];
            }
        }

        /// <summary>
        /// Clears all words from the queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Adds a word to the back of the queue.
        /// </summary>
        /// <param name="word">Word to add.</param>
        /// <exception cref="InvalidOperationException">Thrown if the queue is full.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushBack(ushort word)
        {
            if (_count >= _capacity)
                throw new InvalidOperationException("Prefetch queue is full.");

            int tail = (_head + _count) % _capacity;
            _buffer[tail] = word;
            _count++;
        }

        /// <summary>
        /// Removes and returns the word from the front of the queue.
        /// </summary>
        /// <returns>The word at the front of the queue.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort PopFront()
        {
            if (_count == 0)
                throw new InvalidOperationException("Prefetch queue is empty.");

            ushort value = _buffer[_head];
            _head = (_head + 1) % _capacity;
            _count--;
            return value;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the queue from front to back.
        /// </summary>
        /// <returns>An enumerator for the queue entries.</returns>
        public IEnumerator<ushort> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[(_head + i) % _capacity];
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the queue.
        /// </summary>
        /// <returns>An enumerator for the queue entries.</returns>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}