namespace ChunkedStream
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    public unsafe sealed class ChunkPool : IChunkPool
    {
        private const int MaxBufferLength = 0x7FFFFFC7;
        private const int MinChunkSize = 4;
        private const int MinChunkCount = 1;
        private const int DefaultChunkSize = 64 * 1024;
        private const int DefaultChunkCount = 64;

        private readonly byte[] buffer;
        private readonly int chunkSize;
        private int chunkCount;
        private int next;
        private SpinLock spinLock;

        public int ChunkSize => chunkSize;
        public int ChunkCount => chunkCount;

        public ChunkPool()
            : this(DefaultChunkSize, DefaultChunkCount)
        {
        }

        public ChunkPool(int chunkSize, int chunkCount)
        {
            if (chunkSize < MinChunkSize) throw new Exception();
            if (chunkCount < MinChunkCount) throw new Exception();
            if ((long)chunkSize * chunkCount > MaxBufferLength) throw new Exception();

            this.buffer = new byte[chunkSize * chunkCount];
            this.chunkSize = chunkSize;
            this.chunkCount = chunkCount;
            InitializeBuffer(buffer, chunkSize);
        }

        public bool TryRentFromPool(out Chunk chunk, bool clear = false)
        {
            var offset = GetNextOffset();
            chunk = default(Chunk);

            if (offset != Chunk.InvalidHandle)
            {
                chunk = CreateChunkFromPool(offset, clear);
                return true;
            }

            return false;
        }

        public Chunk Rent(bool clear = false)
        {
            var offset = GetNextOffset();

            if (offset != Chunk.InvalidHandle)
            {
                return CreateChunkFromPool(offset, clear);
            }

            return CreateChunkFromMemory();
        }

        public void Return(ref Chunk chunk)
        {
            if (chunk.IsFromPool)
            {
                if (chunk.Data.Array != buffer)
                {
                    throw new InvalidOperationException();
                }

                Return(chunk.Handle);
                Interlocked.Increment(ref chunkCount);
            }

            chunk = default;
        }

        public bool TryReturn(ref Chunk chunk)
        {
            if (chunk.IsFromPool && chunk.Data.Array == buffer)
            {
                Return(chunk.Handle);
                Interlocked.Increment(ref chunkCount);

                chunk = default;
                return true;
            }

            return false;
        }

        private Chunk CreateChunkFromPool(int offset, bool clear)
        {
            Interlocked.Decrement(ref chunkCount);

            if (clear)
            {
                Array.Clear(buffer, offset, chunkSize);
            }

            return new Chunk(offset, new ArraySegment<byte>(buffer, offset, chunkSize));
        }

        private Chunk CreateChunkFromMemory()
        {
            return new Chunk(new byte[chunkSize]);
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
            bool lockTaken = false;

            try
            {
                spinLock.Enter(ref lockTaken);

                if (next != Chunk.InvalidHandle)
                {
                    var offset = this.next;
                    this.next = *(int*)(pbuff + next);
                    return offset;
                }
            }
            finally
            {
                if (lockTaken) spinLock.Exit();
            }

            return Chunk.InvalidHandle;
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
