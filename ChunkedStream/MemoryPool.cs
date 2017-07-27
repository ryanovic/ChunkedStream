using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ChunkedStream.Chunks;

namespace ChunkedStream
{
    public sealed unsafe class MemoryPool
    {
        public const int InvalidHandler = -1;
        public const int MaxChunkSize = 1 << 30;

        // returns minimal i (for i >= 2) such that 2^i >= num
        private static int GetShiftForNum(int num)
        {
            int shift = 2;
            while (1 << shift < num) { shift++; }
            return shift;
        }

        private readonly object _syncRoot = new object();

        // chunkSize is forced to be equal 1 << chunkSizeShift (2 ^ chunkSizeShift)
        private readonly int _chunkSize;
        private readonly int _chunkSizeShift;
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
            if (chunkSize <= 0 || chunkSize > MaxChunkSize)
                throw new ArgumentException($"chunkSize must be positive and less than or equal 2^30", "chunkSize");

            // align chunkSize to be 2^chunkSizeShift
            _chunkSizeShift = GetShiftForNum(chunkSize);
            _chunkSize = 1 << _chunkSizeShift;

            int maxChunkCount = Int32.MaxValue >> _chunkSizeShift;

            if (chunkCount <= 0 || chunkCount > maxChunkCount)
                throw new ArgumentException($"chunkCount must be positive and less than or equal {maxChunkCount} for chunks with 2^{_chunkSizeShift} size", "chunkCount");


            _chunkCount = chunkCount;

            _top = 0;
            // create buffer
            InitializeBuffer();
        }

        private void InitializeBuffer()
        {
            _buffer = new byte[_chunkSize * _chunkCount];

            fixed (byte* pbuff = &_buffer[0])
            {
                // initialize each chunk to have reference to the next free chunk in its first 4 bytes
                for (int i = 0; i < _chunkCount; i++)
                {
                    *(int*)(pbuff + (i << _chunkSizeShift)) = (i < _chunkCount - 1) ? i + 1 : InvalidHandler;
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

            fixed (byte* pbuff = &_buffer[0])
            {
                lock (_syncRoot)
                {
                    if (_top != InvalidHandler)
                    {
                        handle = _top;
                        _top = *(int*)(pbuff + (handle << _chunkSizeShift));
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

            fixed (byte* pbuff = &_buffer[0])
            {
                lock (_syncRoot)
                {
                    *(int*)(pbuff + (handle << _chunkSizeShift)) = _top;
                    _top = handle;
                    _totalAllocated--;

                    handle = InvalidHandler;
                }
            }
        }

        public int GetChunkOffset(int handle)
        {
            VerifyHandle(handle);

            return handle << _chunkSizeShift;
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
                Array.Clear(Buffer, handle << _chunkSizeShift, _chunkSize);
            }
        }
    }
}
