using System;
using System.Collections.Generic;
using System.Linq;

namespace ChunkedStream.Chunks
{
    public sealed class MemoryPoolChunk : IChunk
    {
        private readonly MemoryPool _owner;
        private readonly int _offset;
        private int _handle;

        public byte[] Buffer
        {
            get
            {
                if (_handle == MemoryPool.InvalidHandler)
                    throw new ObjectDisposedException(null);

                return _owner.Buffer;
            }
        }

        public int Offset
        {
            get
            {
                if (_handle == MemoryPool.InvalidHandler)
                    throw new ObjectDisposedException(null);

                return _offset;
            }
        }

        public int Length
        {
            get
            {
                if (_handle == MemoryPool.InvalidHandler)
                    throw new ObjectDisposedException(null);

                return _owner.ChunkSize;
            }
        }

        public MemoryPoolChunk(MemoryPool owner, int handle)
        {
            if (owner == null)
                throw new ArgumentNullException("owner");

            _owner = owner;
            _handle = handle;
            _offset = owner.GetChunkOffset(handle);
        }

        private void Release()
        {
            if (_handle != MemoryPool.InvalidHandler)
            {
                _owner.ReleaseChunkHandle(ref _handle);
            }
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        ~MemoryPoolChunk()
        {
            Release();

        }
    }
}
