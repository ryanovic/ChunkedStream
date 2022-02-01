namespace Ry.IO
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// <see cref="IChunkPool"/> interface implementation that uses a shared single buffer divided in multiple chunks.
    /// </summary>
    public unsafe sealed class ChunkPool : IChunkPool
    {
        private static long totalPoolAllocated;
        private static long totalMemoryAllocated;

        /// <summary>
        /// Returns a total number of bytes currently allocated in all buffers.
        /// </summary>
        public static long TotalPoolAllocated => Interlocked.Read(ref totalPoolAllocated);

        /// <summary>
        /// Returns a total number of bytes currently allocated on the heap, when a pool failed to return a chunk from its buffer.
        /// </summary>
        public static long TotalMemoryAllocated => Interlocked.Read(ref totalMemoryAllocated);

        private const int MaxBufferLength = 0x7FFFFFC7;
        private const int MinChunkSize = 4;
        private const int MinChunkCount = 1;
        private const int DefaultChunkSize = 64 * 1024;
        private const int DefaultChunkCount = 64;

        private readonly byte[] buffer;
        private readonly int chunkSize;
        private int next;
        private SpinLock spinLock;

        public int ChunkSize => chunkSize;

        public ChunkPool()
            : this(DefaultChunkSize, DefaultChunkCount)
        {
        }

        public ChunkPool(int chunkSize, int chunkCount)
        {
            if (chunkSize < MinChunkSize) throw new ArgumentOutOfRangeException(nameof(chunkSize), Errors.PoolMinChunkSize(MinChunkSize));
            if (chunkCount < MinChunkCount) throw new ArgumentOutOfRangeException(nameof(chunkCount), Errors.PoolMinChunkCount(MinChunkCount));
            if ((long)chunkSize * chunkCount > MaxBufferLength) throw new InvalidOperationException(Errors.PoolMaxBufferSize(MaxBufferLength));

            this.buffer = new byte[chunkSize * chunkCount];
            this.chunkSize = chunkSize;
            InitializeBuffer(buffer, chunkSize);
        }

        /// <summary>
        /// Gets a chunk from the pool if available.
        /// </summary>
        public bool TryRentFromPool(out Chunk chunk, bool clear = false)
        {
            chunk = default(Chunk);

            var offset = GetNextOffset();

            if (offset != Chunk.InvalidHandle)
            {
                chunk = CreateChunkFromPool(offset, clear);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a chunk from the pool, if available, or from the heap otherwise.
        /// </summary>
        public Chunk Rent(bool clear = false)
        {
            var offset = GetNextOffset();

            if (offset != Chunk.InvalidHandle)
            {
                return CreateChunkFromPool(offset, clear);
            }

            return CreateChunkFromMemory();
        }

        /// <summary>
        /// Returns a chunk to the pool. 
        /// </summary>
        public void Return(ref Chunk chunk)
        {
            if (chunk.IsNull) throw new ArgumentException(Errors.ChunkIsNull(), nameof(chunk));

            if (chunk.IsFromPool)
            {
                if (chunk.Data.Array != buffer)
                {
                    throw new InvalidOperationException(Errors.ChunkIsImposter());
                }

                Interlocked.Add(ref totalPoolAllocated, -ChunkSize);
                Return(chunk.Handle);
            }
            else
            {
                Interlocked.Add(ref totalMemoryAllocated, -ChunkSize);
            }

            chunk = default;
        }

        /// <summary>
        /// Check if the chunk belongs to this pool.
        /// </summary>
        public bool IsFromPool(in Chunk chunk)
        {
            return chunk.IsFromPool && chunk.Data.Array == buffer;
        }

        private Chunk CreateChunkFromPool(int offset, bool clear)
        {
            if (clear)
            {
                Array.Clear(buffer, offset, chunkSize);
            }

            var chunk = new Chunk(offset, new ArraySegment<byte>(buffer, offset, chunkSize));
            Interlocked.Add(ref totalPoolAllocated, ChunkSize);
            return chunk;
        }

        private Chunk CreateChunkFromMemory()
        {
            var chunk = new Chunk(new byte[chunkSize]);
            Interlocked.Add(ref totalMemoryAllocated, ChunkSize);
            return chunk;
        }

        private int GetNextOffset()
        {
            fixed (byte* pbuff = buffer)
            {
                if (next != Chunk.InvalidHandle)
                {
                    return GetNextOffset(pbuff);
                }

                return Chunk.InvalidHandle;
            }
        }

        private int GetNextOffset(byte* pbuff)
        {
            var lockTaken = false;
            var offset = Chunk.InvalidHandle;

            try
            {
                spinLock.Enter(ref lockTaken);

                if (next != Chunk.InvalidHandle)
                {
                    offset = this.next;
                    this.next = *(int*)(pbuff + next);
                }
            }
            finally
            {
                if (lockTaken) spinLock.Exit();
            }

            return offset;
        }

        private void Return(int offset)
        {
            fixed (byte* pbuff = buffer)
            {
                bool lockTaken = false;

                try
                {
                    spinLock.Enter(ref lockTaken);

                    *(int*)(pbuff + offset) = this.next;
                    this.next = offset;
                }
                finally
                {
                    if (lockTaken) spinLock.Exit();
                }
            }
        }

        private static void InitializeBuffer(byte[] buffer, int chunkSize)
        {
            fixed (byte* pbuff = buffer)
            {
                int offset = 0, next = chunkSize;

                while (next < buffer.Length)
                {
                    *(int*)(pbuff + offset) = next;
                    offset = next;
                    next += chunkSize;
                }

                *(int*)(pbuff + offset) = Chunk.InvalidHandle;
            }
        }
    }
}