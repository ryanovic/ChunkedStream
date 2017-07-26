using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChunkedStream.Chunks
{
    public sealed class MemoryChunk : IChunk
    {
        private readonly byte[] _buffer;

        public byte[] Buffer
        {
            get
            {
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
                return _buffer.Length;
            }
        }

        public MemoryChunk(int length)
        {
            _buffer = new byte[length];
        }

        public void Dispose()
        {
        }
    }
}
