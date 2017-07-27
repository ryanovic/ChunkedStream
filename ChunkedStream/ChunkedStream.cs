using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ChunkedStream.Chunks;

namespace ChunkedStream
{
    public sealed class ChunkedStream : Stream
    {
        private const int DefaultMemmoryChunkSize = 512;

        private static MemoryPool _defaultPool = null;

        public static void InitializePool(int chunkSize = 4096, int chunkCount = 1000)
        {
            if (_defaultPool != null)
                throw new InvalidOperationException("Pool is already initialized");

            _defaultPool = new MemoryPool(chunkSize, chunkCount);
        }

        public static ChunkedStream FromMemory(int memoryChunkSize = DefaultMemmoryChunkSize)
        {
            if (memoryChunkSize <= 0 || memoryChunkSize > MemoryPool.MaxChunkSize)
                throw new ArgumentException($"memoryChunkSize must be positive and less than or equal 2^30", "memoryChunkSize");

            return new ChunkedStream(null, memoryChunkSize);
        }

        public static ChunkedStream FromPool(MemoryPool pool)
        {
            if (pool == null && _defaultPool == null)
                throw new ArgumentNullException("pool", "pool must be specified in case when no default pool is configured");

            return new ChunkedStream(pool ?? _defaultPool, -1);
        }

        private readonly MemoryPool _pool;
        private readonly int _chunkSize;

        private List<IChunk> chunks = null;

        private int position = 0;
        private int length = 0;

        private ChunkedStream(MemoryPool pool, int memoryChunkSize)
        {
            _pool = pool;
            _chunkSize = _pool?.ChunkSize ?? memoryChunkSize;
        }

        public ChunkedStream(MemoryPool pool = null)
            : this(pool ?? _defaultPool, DefaultMemmoryChunkSize)
        {
        }

        private IChunk CreateChunk()
        {
            return _pool == null
                ? new MemoryChunk(_chunkSize)
                : _pool.GetChunk();
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return true;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return true;
            }
        }

        public override long Length
        {
            get
            {
                return length;
            }
        }

        public override long Position
        {
            get
            {
                return position;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override int ReadByte()
        {
            return base.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void WriteByte(byte value)
        {
            base.WriteByte(value);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

        }
    }
}
