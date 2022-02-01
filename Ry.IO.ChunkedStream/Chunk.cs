namespace Ry.IO
{
    using System;

    /// <summary>
    /// Represents a range of bytes from a shared pool or managed memory.
    /// </summary>
    public readonly struct Chunk
    {
        public const int InvalidHandle = -1;

        public Chunk(byte[] buffer)
            : this(InvalidHandle, new ArraySegment<byte>(buffer))
        {
        }

        public Chunk(int handle, ArraySegment<byte> buffer)
        {
            this.Handle = handle;
            this.Data = buffer;
        }

        /// <summary>
        /// Returns a pool identifier or <see cref="InvalidHandle"/> if a chunk is allocated on the heap. 
        /// </summary>
        public int Handle { get; }

        /// <summary>
        /// Returns a buffer fragment.
        /// </summary>
        public ArraySegment<byte> Data { get; }

        /// <summary>
        /// Returns bytes as a span.
        /// </summary>
        public Span<byte> Span => Data.AsSpan();

        public Span<byte> AsSpan(int start) => Data.AsSpan(start);
        public Span<byte> AsSpan(int start, int length) => Data.AsSpan(start, length);

        public Memory<byte> AsMemory(int start) => Data.AsMemory(start);
        public Memory<byte> AsMemory(int start, int length) => Data.AsMemory(start, length);

        /// <summary>
        /// Gets a value indicating whether a chunk is NOT allocated.
        /// </summary>
        public bool IsNull => Data.Array == null;

        /// <summary>
        /// Gets a value indicating whether a chunk is allocated in a pool.
        /// </summary>
        public bool IsFromPool => !IsNull && Handle != InvalidHandle;

        /// <summary>
        /// Gets a value indicating whether a chunk is allocated on the heap.
        /// </summary>
        public bool IsFromMemory => !IsNull && Handle == InvalidHandle;
    }
}