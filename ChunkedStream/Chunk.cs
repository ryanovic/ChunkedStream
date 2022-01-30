namespace ChunkedStream
{
    using System;
    using System.Collections.Generic;
    using System.Text;

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

        public int Handle { get; }
        public ArraySegment<byte> Data { get; }

        public Span<byte> Span => Data.AsSpan();
        public Span<byte> AsSpan(int start) => Data.AsSpan(start);
        public Span<byte> AsSpan(int start, int length) => Data.AsSpan(start, length);

        public Memory<byte> AsMemory(int start) => Data.AsMemory(start);
        public Memory<byte> AsMemory(int start, int length) => Data.AsMemory(start, length);

        public bool IsNull => Data.Array == null;
        public bool IsFromPool => !IsNull && Handle != InvalidHandle;
        public bool IsFromMemory => !IsNull && Handle == InvalidHandle;
    }
}
