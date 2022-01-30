namespace ChunkedStream
{
    using System;
    using System.Buffers;

    public static class ChunkArrayPool
    {
        public static ArrayPool<Chunk> Empty { get; } = new EmptyChunkArrayPool();

        public static ArrayPool<Chunk> Create() => ArrayPool<Chunk>.Create();

        public static ArrayPool<Chunk> Create(int maxArrayLength, int maxArraysPerBucket) => ArrayPool<Chunk>.Create(maxArrayLength, maxArraysPerBucket);
    }
}
