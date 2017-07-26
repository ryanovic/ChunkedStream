using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChunkedStream
{
    public sealed unsafe class MemoryPool
    {
        public const int InvalidHandler = -1;

        // returns minimal i (for i >= 2) such that 2^i >= num
        private static int GetShiftForNum(int num)
        {
            int shift = 2;
            while (1 << shift <= num) { shift++; }
            return shift;
        }

        private readonly SpinLock _lock = new SpinLock(enableThreadOwnerTracking: false);

        // chunkSize is forced to be equal 1 << chunkSizeShift (2 ^ chunkSizeShift)
        private readonly int _chunkSize;
        private readonly int _chunkSizeShift;
        private readonly int _chunkCount;

        private byte[] _buffer;
        private int _top;

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

        public MemoryPool(int chunkSize = 4096, int chunkCount = 1000)
        {
            // align chunkSize to be 2^chunkSizeShift
            _chunkSizeShift = GetShiftForNum(chunkSize);
            _chunkSize = 1 << _chunkSizeShift;
            _chunkCount = chunkCount;
            _top = 0;
            // create buffer
            InitializeBuffer();
        }

        private void InitializeBuffer()
        {
            int current = 0;
            _buffer = new byte[_chunkSize * _chunkCount];

            // initialize each chunk to have reference to the next free chunk in its first 4 bytes
            fixed (byte* pbuff = &_buffer[0])
            {
                while (current < _chunkCount - 1)
                {
                    // _buffer[current] = _buffer[current + 1]
                    *(int*)(pbuff + (current << _chunkSizeShift)) = ++current;
                }
                // _buffer[_chunkCount - 1] = -1
                *(int*)(pbuff + (current << _chunkSizeShift)) = InvalidHandler;
            }
        }

        public IChunk TryGetChunkFromPool()
        {
            int handle = TryGetFreeChunkHandle();
            return handle == InvalidHandler ? null : new MemoryPoolChunk(this, handle);
        }

        public IChunk GetChunk()
        {
            return TryGetChunkFromPool() ?? new MemoryChunk(ChunkSize);
        }

        public int TryGetFreeChunkHandle()
        {
            fixed (byte* pbuff = &_buffer[0])
            {
                return TryGetFreeChunkHandle(pbuff);
            }
        }

        private int TryGetFreeChunkHandle(byte* pbuff)
        {
            int index = InvalidHandler; bool lockTaken = false;

            try
            {
                _lock.Enter(ref lockTaken);

                if (_top != InvalidHandler)
                {
                    index = _top;
                    _top = *(int*)(pbuff + (index << _chunkSizeShift));
                }
            }
            finally
            {
                if (lockTaken) _lock.Exit(useMemoryBarrier: false);
            }
            return index;
        }

        public void VerifyHandle(int handle)
        {
            if (handle < 0 || handle >= _chunkCount)
                throw new InvalidOperationException($"Invalid Handle ({handle})");
        }

        public void ReleaseChunkHandle(ref int handle)
        {
            VerifyHandle(handle);

            fixed (byte* pbuff = &_buffer[0])
            {
                ReleaseChunkHandle(pbuff, ref handle);
            }
        }

        private void ReleaseChunkHandle(byte* pbuff, ref int handle)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_top != -1)
                {
                    *(int*)(pbuff + (handle << _chunkSizeShift)) = _top;
                }
                _top = handle;
                handle = InvalidHandler;
            }
            finally
            {
                if (lockTaken) _lock.Exit(useMemoryBarrier: false);
            }
        }

        public int GetChunkOffset(int handle)
        {
            VerifyHandle(handle);

            return handle << _chunkSizeShift;
        }
    }
}
