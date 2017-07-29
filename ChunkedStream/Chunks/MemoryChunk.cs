using System;
using System.Collections.Generic;
using System.Linq;

namespace ChunkedStream.Chunks
{
    public sealed class MemoryChunk : IChunk
    {
        private byte[] _buffer;

        public byte[] Buffer
        {
            get
            {
                if (_buffer == null)
                    throw new ObjectDisposedException(null);

                return _buffer;
            }
        }

        public int Offset
        {
            get
            {
                return 0;
            }
        }

        public int Length
        {
            get
            {
                if (_buffer == null)
                    throw new ObjectDisposedException(null);

                return _buffer.Length;
            }
        }

        public MemoryChunk(int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentException($"chunkSize must be greater than 0", "chunkSize");

            _buffer = new byte[chunkSize];
        }

        public void Dispose()
        {
            _buffer = null;
        }
    }
}
