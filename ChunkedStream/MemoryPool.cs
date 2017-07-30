using System;
using System.Collections.Generic;

using ChunkedStream.Chunks;

namespace ChunkedStream
{
    public sealed unsafe class MemoryPool
    {
        public const int InvalidHandler = -1;
        public const int MinChunkSize = 4;

        private readonly object _syncRoot = new object();

        private readonly int _chunkSize;
        private readonly int _chunkCount;

        private byte[] _buffer;
        private int _top;
        private int _totalAllocated = 0;

        public byte[] Buffer
        {
            get
            {
                return _buffer;
            }
        }

        public int ChunkSize
        {
            get
            {
                return _chunkSize;
            }
        }

        public int ChunkCount
        {
            get
            {
                return _chunkCount;
            }
        }

        public int TotalAllocated
        {
            get
            {
                return _totalAllocated;
            }
        }

        public MemoryPool(int chunkSize = 4096, int chunkCount = 1000)
        {
            if (chunkSize < MinChunkSize)
                throw new ArgumentOutOfRangeException($"Chunk Size must be positive and greater than or equal {MinChunkSize}", "chunkCount");

            if (chunkCount <= 0 || chunkCount > (Int32.MaxValue / chunkSize))
                throw new ArgumentOutOfRangeException($"Chunk Count must be positive and less than or equal {Int32.MaxValue / chunkSize} for chunks with {chunkSize} size", "chunkCount");

            _chunkSize = chunkSize;
            _chunkCount = chunkCount;

            _top = 0;
            InitializeBuffer();
        }

        private void InitializeBuffer()
        {
            _buffer = new byte[_chunkSize * _chunkCount];

            fixed (byte* pbuff = _buffer)
            {
                // initialize each chunk to have reference to the next free chunk in its first 4 bytes
                for (int i = 0; i < _chunkCount; i++)
                {
                    *(int*)(pbuff + (i * _chunkSize)) = (i < _chunkCount - 1) ? i + 1 : InvalidHandler;
                }
            }
        }

        public IChunk TryGetChunkFromPool()
        {
            int handle = TryGetChunkHandle();

            return handle == InvalidHandler
                ? null
                : new MemoryPoolChunk(this, handle);
        }

        public IChunk GetChunk()
        {
            return TryGetChunkFromPool() ?? new MemoryChunk(ChunkSize);
        }

        public int TryGetChunkHandle()
        {
            int handle = InvalidHandler;

            fixed (byte* pbuff = _buffer)
            {
                lock (_syncRoot)
                {
                    if (_top != InvalidHandler)
                    {
                        handle = _top;
                        _top = *(int*)(pbuff + (handle * _chunkSize));
                        _totalAllocated++;
                    }
                }
            }

            ZerroChunk(handle);
            return handle;
        }

        public void ReleaseChunkHandle(ref int handle)
        {
            VerifyHandle(handle);

            fixed (byte* pbuff = _buffer)
            {
                lock (_syncRoot)
                {
                    *(int*)(pbuff + (handle * _chunkSize)) = _top;
                    _top = handle;
                    _totalAllocated--;

                    handle = InvalidHandler;
                }
            }
        }

        public int GetChunkOffset(int handle)
        {
            VerifyHandle(handle);
            return handle * _chunkSize;
        }

        public void VerifyHandle(int handle)
        {
            if (handle < 0 || handle >= _chunkCount)
                throw new InvalidOperationException($"Invalid Handle ({handle})");
        }

        private void ZerroChunk(int handle)
        {
            if (handle != -1)
            {
                Array.Clear(Buffer, handle * _chunkSize, _chunkSize);
            }
        }
    }
}
