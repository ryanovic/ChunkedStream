using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ChunkedStream.Chunks;
using System.Threading;

namespace ChunkedStream
{
    public sealed class ChunkedStream : Stream
    {
        private const int DefaultMemoryChunkSize = 512;
        private const int DefaultChunkCount = 4;

        private static MemoryPool _defaultPool = null;

        public static void DisablePool()
        {
            _defaultPool = null;
        }

        public static void InitializePool(int chunkSize = 4096, int chunkCount = 1000)
        {
            if (_defaultPool != null)
                throw new InvalidOperationException("Pool is already initialized");

            _defaultPool = new MemoryPool(chunkSize, chunkCount);
        }

        public static ChunkedStream FromMemory(int memoryChunkSize = DefaultMemoryChunkSize)
        {
            return new ChunkedStream(null, memoryChunkSize);
        }

        public static ChunkedStream FromPool(MemoryPool pool = null)
        {
            if (pool == null && _defaultPool == null)
                throw new ArgumentNullException("pool", "pool must be specified in case when no default pool is configured");

            return new ChunkedStream(pool ?? _defaultPool, -1);
        }

        private readonly MemoryPool _pool;
        private readonly int _chunkSize;
        private readonly int _chunkShift;
        private readonly int _offsetMask;

        private IChunk[] _chunks = null;

        private long _position = 0;
        private long _length = 0;

        private ChunkedStreamState _state = ChunkedStreamState.ReadWrite;

        private ChunkedStream(MemoryPool pool, int memoryChunkSize)
        {
            if (pool != null)
            {
                _pool = pool;
                _chunkSize = pool.ChunkSize;
                _chunkShift = ChunkHelper.GetAlignment(_chunkSize);
            }
            else
            {
                _chunkSize = ChunkHelper.AlignChunkSize(memoryChunkSize, out _chunkShift);
            }
            _offsetMask = ~((-1) << _chunkShift);
        }

        public ChunkedStream(MemoryPool pool = null)
            : this(pool ?? _defaultPool, DefaultMemoryChunkSize)
        {
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
                return _length;
            }
        }

        public override long Position
        {
            get
            {
                return _position;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "");

                _position = value;
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position = checked(_position + offset);
                    break;
                case SeekOrigin.End:
                    Position = checked(_length + offset);
                    break;
            }
            return _position;
        }

        public override void SetLength(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException("value", "");

            _length = value;
            _position = Math.Min(_position, _length);

            ReleaseRight();
        }

        private static void VerifyArguments(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset", "offset must be non-negative");

            if (count < 0)
                throw new ArgumentOutOfRangeException("count", "count must be non-negative");

            if (buffer.Length - offset < count)
                throw new ArgumentException(String.Empty, "offset + count must be less than buffer.Length");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;

            VerifyArguments(buffer, offset, count);

            int chunkOffset, totalRead = 0;

            while (count > 0 && _position < _length)
            {
                var chunk = GetChunkForRead(out chunkOffset);

                int toRead = (int)Math.Min(_length - _position, Math.Min(count, _chunkSize - chunkOffset));

                if (chunk == null)
                {
                    Array.Clear(buffer, offset, toRead);
                }
                else
                {
                    Buffer.BlockCopy(chunk.Buffer, chunk.Offset + chunkOffset, buffer, offset, toRead);
                }

                totalRead += toRead;
                _position += toRead;
                offset += toRead;
                count -= toRead;
            }

            return totalRead;
        }

        public override int ReadByte()
        {
            if (_position >= _length)
                return -1;

            int offset;
            var chunk = GetChunkForRead(out offset);
            int value = chunk?.Buffer[chunk.Offset + offset] ?? 0;
            _position++;

            return value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count == 0) return;

            VerifyArguments(buffer, offset, count);

            while (count > 0)
            {
                int chunkOffset;
                var chunk = GetChunkForWrite(out chunkOffset);

                int toWrite = Math.Min(count, _chunkSize - chunkOffset);
                Buffer.BlockCopy(buffer, offset, chunk.Buffer, chunk.Offset + chunkOffset, toWrite);

                _position += toWrite;
                offset += toWrite;
                count -= toWrite;
            }
            _length = Math.Max(_length, _position);
        }

        public override void WriteByte(byte value)
        {
            int offset;
            var chunk = GetChunkForWrite(out offset);
            chunk.Buffer[chunk.Offset + offset] = value;

            _length = Math.Max(_length, ++_position);
        }

        public void AsOutputStream(int fromPosition = 0)
        {
            _position = fromPosition;
            _state = ChunkedStreamState.ReadForward;


        }

        protected override void Dispose(bool disposing)
        {
            if (_chunks != null)
            {
                for (int i = 0; i < _chunks.Length; i++)
                {
                    _chunks[i]?.Dispose();
                    _chunks[i] = null;
                }
            }
            base.Dispose(disposing);
        }

        private IChunk CreateChunk()
        {
            return _pool == null
                ? new MemoryChunk(_chunkSize)
                : _pool.GetChunk();
        }

        private IChunk GetChunkForRead(out int offset)
        {
            int chunkIndex = GetChunkIndex(_position, out offset);
            return _chunks?[chunkIndex];
        }

        private IChunk GetChunkForWrite(out int offset)
        {
            int chunkIndex = GetChunkIndex(_position, out offset);
            EnsureCapacity(chunkIndex);

            var chunk = _chunks[chunkIndex];
            if (chunk == null)
            {
                chunk = CreateChunk();
                _chunks[chunkIndex] = chunk;
            }

            return chunk;
        }

        private void EnsureCapacity(int chunkIndex)
        {
            if (_chunks == null)
            {
                _chunks = new IChunk[Math.Max(DefaultChunkCount, chunkIndex + 1)];
            }
            else if (_chunks.Length <= chunkIndex)
            {
                var tmp = new IChunk[Math.Max(_chunks.Length << 1, chunkIndex + 1)];
                _chunks.CopyTo(tmp, 0);
                _chunks = tmp;
            }
        }

        private void ReleaseRight()
        {
            if (_chunks != null)
            {
                int offset, chunkIndex = GetChunkIndex(_length, out offset) + Math.Sign(offset);

                for (; chunkIndex < _chunks.Length; chunkIndex++)
                {
                    _chunks[chunkIndex]?.Dispose();
                    _chunks[chunkIndex] = null;
                }
            }
        }

        private void ReleaseLeft()
        {
            if (_chunks != null)
            {
                int offset, chunkIndex = GetChunkIndex(_position, out offset);

                for (int i = 0; i < chunkIndex; i++)
                {
                    _chunks[i]?.Dispose();
                    _chunks[i] = null;
                }
            }
        }

        private int GetChunkIndex(long position, out int offset)
        {
            int chunkIndex = checked((int)(position >> _chunkShift));
            offset = (int)(position & _offsetMask);
            return chunkIndex;
        }
    }
}
