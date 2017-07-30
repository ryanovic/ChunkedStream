using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using ChunkedStream.Chunks;

namespace ChunkedStream
{
    public unsafe sealed class ChunkedStream : Stream
    {
        private const int DefaultMemoryChunkSize = 512;
        private const int DefaultChunksCapacity = 4;

        private static MemoryPool _defaultPool = null;

        /// <summary>
        /// Set global default pool.
        /// </summary>
        public static void SetMemoryPool(MemoryPool pool)
        {
            _defaultPool = pool;
        }

        /// <summary>
        /// Initializes new global pool will be shared by all ChunkedStream instances by default.
        /// </summary>
        /// <param name="chunkSize">Chunk Size - will be aligned to nearest 2^n value.</param>
        /// <param name="chunkCount">Chunk count in pool.</param>
        public static void InitializePool(int chunkSize = 4096, int chunkCount = 1000)
        {
            if (_defaultPool != null)
                throw new InvalidOperationException("Pool is already initialized");

            _defaultPool = new MemoryPool(chunkSize, chunkCount);
        }

        /// <summary>
        /// Creates a new instance of ChunkedStream which will use chunks from managed heap only.
        /// </summary>
        /// <param name="memoryChunkSize">Chunk Size - will be aligned to nearest 2^n value.</param>
        /// <returns></returns>
        public static ChunkedStream FromMemory(int memoryChunkSize = DefaultMemoryChunkSize)
        {
            if (memoryChunkSize < MemoryPool.MinChunkSize)
                throw new ArgumentException($"Chunk Size must be positive and greater than or equal {MemoryPool.MinChunkSize}", "memoryChunkSize");

            return new ChunkedStream(null, memoryChunkSize);
        }

        /// <summary>
        /// Creates a new instance of ChunkedStream.
        /// If <paramref name="pool"/> is null - default memory pool will be used instead.
        /// If default memory pool is not initialized and <paramref name="pool"/> is null
        /// then ArgumentNullException exception will be thrown.
        /// </summary>
        /// <param name="pool">Optional MemoryPool instances.</param>
        public static ChunkedStream FromPool(MemoryPool pool = null)
        {
            if (pool == null && _defaultPool == null)
                throw new ArgumentNullException("pool", "pool must be specified in case when no default pool is configured");

            return new ChunkedStream(pool ?? _defaultPool, -1);
        }

        // MemoryPool instance. If it's null - managed heap is used
        private readonly MemoryPool _pool;
        private readonly int _chunkSize;

        private IChunk[] _chunks = new IChunk[DefaultChunksCapacity];

        private long _position = 0;
        private long _length = 0;

        private ChunkedStreamState _state = ChunkedStreamState.ReadWrite;

        private ChunkedStream(MemoryPool pool, int memoryChunkSize)
        {
            if (pool != null)
            {
                // work with pool
                _pool = pool;
                _chunkSize = pool.ChunkSize;
            }
            else
            {
                // managed heap only                
                _chunkSize = memoryChunkSize;
            }
        }

        /// <summary>
        /// Creates a new instance of ChunkedStream.
        /// If <paramref name="pool"/> is null - default memory pool will be used instead.
        /// If default memory pool is not initialized and <paramref name="pool"/> is null
        /// then all chunks will be allocated on the managed heap.
        /// </summary>
        /// <param name="pool">Optional MemoryPool instances.</param>
        public ChunkedStream(MemoryPool pool = null)
            : this(pool ?? _defaultPool, DefaultMemoryChunkSize)
        {
        }

        public override bool CanRead
        {
            get
            {
                return _state != ChunkedStreamState.Closed;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return _state == ChunkedStreamState.ReadWrite;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return _state == ChunkedStreamState.ReadWrite;
            }
        }

        // gets Length
        public override long Length
        {
            get
            {
                #region Validate

                if (_state == ChunkedStreamState.Closed)
                    throw new ObjectDisposedException(null);

                #endregion

                return _length;
            }
        }

        // gets or sets Position
        public override long Position
        {
            get
            {
                #region Validate

                if (_state == ChunkedStreamState.Closed)
                    throw new ObjectDisposedException(null);

                #endregion

                return _position;
            }
            set
            {
                #region Validate

                if (_state == ChunkedStreamState.Closed)
                    throw new ObjectDisposedException(null);

                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (value < _position && _state == ChunkedStreamState.ReadForward)
                    throw new InvalidOperationException();

                #endregion

                _position = value;

                if (_state == ChunkedStreamState.ReadForward)
                {
                    // in case AsOutputString is called release all chunks in the left
                    ReleaseLeft();
                }
            }
        }

        // nothing to do
        public override void Flush()
        {
        }

        // move position
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

        // update length for current stream
        public override void SetLength(long value)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            #endregion

            _length = value;
            _position = Math.Min(_position, _length);

            // release chunks outside the current length
            ReleaseRight();
        }

        // reads bytes from the stream
        public int Read(byte* pbuff, int count)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            #endregion

            count = (int)Math.Min(count, _length - _position);

            int offset, totalRead = 0;

            while (count > 0)
            {
                var chunk = GetChunkWithOffset(out offset);
                int toRead = Math.Min(count, _chunkSize - offset);

                fixed (byte* pchunk = &chunk.Buffer[chunk.Offset + offset])
                {
                    Buffer.MemoryCopy(pchunk, pbuff, toRead, toRead);
                }

                if (_state == ChunkedStreamState.ReadForward && (toRead + offset) == _chunkSize)
                {
                    // means chunk is completed now and can be released
                    chunk.Dispose();
                }

                _position += toRead;
                pbuff += toRead;
                totalRead += toRead;
                count -= toRead;
            }

            return totalRead;
        }

        // reads bytes from the stream
        public override int Read(byte[] buffer, int offset, int count)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (buffer == null)
                throw new NullReferenceException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset) + " + " + nameof(count));

            #endregion

            if (count > 0)
            {
                fixed (byte* pbuff = &buffer[offset])
                {
                    return Read(pbuff, count);
                }
            }
            return 0;
        }

        // gets byte from the stream
        public override int ReadByte()
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            #endregion     

            // End of stream
            if (_position >= _length) return -1;

            int offset, value;
            var chunk = GetChunkWithOffset(out offset);
            value = chunk.Buffer[chunk.Offset + offset];

            if (_state == ChunkedStreamState.ReadForward && offset == _chunkSize - 1)
            {
                // means chunk is completed now and can be released
                chunk.Dispose();
            }

            _position++;
            return value;
        }

        // puts bytes from pointer on the stream
        public void Write(byte* pbuff, int count)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            #endregion

            while (count > 0)
            {
                int offset;
                var chunk = GetChunkWithOffset(out offset);

                fixed (byte* pchunk = &chunk.Buffer[chunk.Offset + offset])
                {
                    int toWrite = Math.Min(count, _chunkSize - offset);
                    Buffer.MemoryCopy(pbuff, pchunk, toWrite, toWrite);

                    _position = checked(_position + toWrite);
                    pbuff += toWrite;
                    count -= toWrite;
                }
            }

            _length = Math.Max(_length, _position);
        }

        // puts bytes from buffer on the stream
        public override void Write(byte[] buffer, int offset, int count)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            if (buffer == null)
                throw new NullReferenceException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset) + " + " + nameof(count));

            #endregion

            if (count > 0)
            {
                fixed (byte* pbuff = &buffer[offset])
                {
                    Write(pbuff, count);
                }
            }
        }

        // puts bytes from buffer on the stream
        public void Write(byte[] buffer)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            #endregion

            if (buffer != null && buffer.Length > 0)
            {
                fixed (byte* pbuff = buffer)
                {
                    Write(pbuff, buffer.Length);
                }
            }
        }

        // puts byte on the stream
        public override void WriteByte(byte value)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            #endregion            

            int offset;
            var chunk = GetChunkWithOffset(out offset);
            chunk.Buffer[chunk.Offset + offset] = value;
            _length = Math.Max(_length, checked(++_position));
        }

        // gets stream bytes
        public byte[] ToArray()
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            #endregion

            return ToArray(0, (int)Length);
        }

        // gets specified number of bytes from the stream started from provided offset
        public byte[] ToArray(int offset, int count)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            #endregion

            long tmp = _position;
            _position = offset;

            var buffer = new byte[count];

            if (count > 0)
            {
                fixed (byte* pbuff = buffer)
                {
                    Read(pbuff, count);
                }
            }

            _position = tmp;
            return buffer;
        }

        // release all chunks
        public void Clear()
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            #endregion

            _position = _length = 0;

            for (int i = 0; i < _chunks.Length; i++)
            {
                _chunks[i]?.Dispose();
                _chunks[i] = null;
            }
        }

        /// <summary>
        /// Moves current stream into ReadForward state. Means that only sequential read operations are allowed.
        /// Each chunk will be immediately released and put back into pool once it's processed.
        /// </summary>
        /// <param name="fromPosition">Initial poistion current stream will be read from.</param>
        public void AsOutputStream(int fromPosition = 0)
        {
            #region Validate

            if (_state == ChunkedStreamState.Closed)
                throw new ObjectDisposedException(null);

            if (_state == ChunkedStreamState.ReadForward)
                throw new InvalidOperationException();

            if (fromPosition < 0)
                throw new ArgumentOutOfRangeException(nameof(fromPosition));

            #endregion

            _position = fromPosition;
            _state = ChunkedStreamState.ReadForward;

            ReleaseLeft();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (_state != ChunkedStreamState.Closed && disposing)
            {
                for (int i = 0; i < _chunks.Length; i++)
                {
                    _chunks[i]?.Dispose();
                    _chunks[i] = null;
                }
                _chunks = null;
                _state = ChunkedStreamState.Closed;
            }
        }

        #region Internal Chunk Managemet

        // get chunk from pool or from managed heap, in case, when pool is empty or not available  
        private IChunk CreateChunk()
        {
            return _pool == null
                ? new MemoryChunk(_chunkSize)
                : _pool.GetChunk(); // pool will retrun chunk from the heap once it's empty
        }

        // gets chunk with local chunk offset corresponding to current position
        private IChunk GetChunkWithOffset(out int offset)
        {
            int index = GetChunkIndexWithOffset(_position, out offset);
            EnsureChunksCapacity(index);

            var chunk = _chunks[index];
            if (chunk == null)
            {
                chunk = CreateChunk();
                _chunks[index] = chunk;
            }

            return chunk;
        }

        // expands _chunks to handle index if necessary
        private void EnsureChunksCapacity(int index)
        {
            if (_chunks.Length <= index)
            {
                var tmp = new IChunk[Math.Max(_chunks.Length * 2, index + 1)];
                _chunks.CopyTo(tmp, 0);
                _chunks = tmp;
            }
        }

        // release chunks beyond the current length
        private void ReleaseRight()
        {
            // if offset > 0 (so chunk is partially used) - skip current chunk or release otherwise
            int offset, index = GetChunkIndexWithOffset(_length, out offset) + Math.Sign(offset);

            for (; index < _chunks.Length; index++)
            {
                _chunks[index]?.Dispose();
                _chunks[index] = null;
            }
        }

        // release chunks beyond the current position
        private void ReleaseLeft()
        {
            int offset, index = GetChunkIndexWithOffset(_position, out offset);

            for (int i = 0; i < index; i++)
            {
                _chunks[i]?.Dispose();
                _chunks[i] = null;
            }
        }

        // get chunk index and offset corresponding position provided
        private int GetChunkIndexWithOffset(long position, out int offset)
        {
            if (position < _chunkSize)
            {
                offset = (int)position;
                return 0;
            }
            else
            {
                offset = (int)(position % _chunkSize);
                return checked((int)(position / _chunkSize));
            }
        }

        #endregion
    }
}
