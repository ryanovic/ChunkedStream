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
        private const int DefaultChunksLength = 4;

        private static MemoryPool _defaultPool = null;

        /// <summary>
        /// Disable global pool.
        /// </summary>
        public static void DisablePool()
        {
            _defaultPool = null;
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
        private readonly int _chunkSize; // _chunkSize = 2 ^ _chunkSizeShift
        private readonly int _chunkSizeShift; // _chunkSizeShift >= 2
        // bit mask to get a local chunk offset from position
        private readonly int _chunkOffsetMask; // chunkOffset = position & _chunkOffsetMask

        private IChunk[] _chunks = null;

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
                _chunkSizeShift = MemoryPool.GetShiftForChunkSize(_chunkSize);
            }
            else
            {
                // managed heap only
                _chunkSizeShift = MemoryPool.GetShiftForChunkSize(memoryChunkSize);
                _chunkSize = 1 << _chunkSizeShift;
            }
            _chunkOffsetMask = ~((-1) << _chunkSizeShift);
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
                VerifyStreamNotInState(ChunkedStreamState.Closed);
                return _length;
            }
        }

        // gets or sets Position
        public override long Position
        {
            get
            {
                VerifyStreamNotInState(ChunkedStreamState.Closed);
                return _position;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "value must be non-negative");

                if (value >= _position)
                {
                    // it's allowed to move forward in any state (except closed)
                    VerifyStreamNotInState(ChunkedStreamState.Closed);
                }
                else
                {
                    // once AsOutputString is called - it's NOT allowed to move back
                    VerifyStreamInState(ChunkedStreamState.ReadWrite);
                }

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
            if (value < 0)
                throw new ArgumentOutOfRangeException("value", "value must be non-negative");

            if (value >= _position)
            {
                VerifyStreamNotInState(ChunkedStreamState.Closed);
            }
            else
            {
                VerifyStreamInState(ChunkedStreamState.ReadWrite);
            }

            _length = value;
            _position = Math.Min(_position, _length);

            // release chunks outside the current length
            ReleaseRight();
        }

        // copy bytes to buffer provided
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;

            VerifyStreamNotInState(ChunkedStreamState.Closed);
            VerifyInputBuffer(buffer, offset, count);

            int chunkOffset, totalRead = 0;

            while (count > 0 && _position < _length)
            {
                var chunk = GetChunkForRead(out chunkOffset);

                int toRead = (int)Math.Min(_length - _position, Math.Min(count, _chunkSize - chunkOffset));

                if (chunk != null)
                {
                    Buffer.BlockCopy(chunk.Buffer, chunk.Offset + chunkOffset, buffer, offset, toRead);

                    if (_state == ChunkedStreamState.ReadForward && (toRead + chunkOffset) == _chunkSize)
                    {
                        // means chunk is completed now and can be released
                        chunk.Dispose();
                    }
                }
                else
                {
                    // when chunk == null means all bytes = 0 for it
                    Array.Clear(buffer, offset, toRead);
                }

                _position += toRead;
                totalRead += toRead;
                offset += toRead;
                count -= toRead;
            }

            return totalRead;
        }

        // gets signle byte from the stream
        public override int ReadByte()
        {
            VerifyStreamNotInState(ChunkedStreamState.Closed);

            if (_position >= _length)
                return -1;

            int chunkOffset, value = 0;
            var chunk = GetChunkForRead(out chunkOffset);
            _position++;

            if (chunk != null)
            {
                value = chunk.Buffer[chunk.Offset + chunkOffset];

                if (_state == ChunkedStreamState.ReadForward && chunkOffset == _chunkSize - 1)
                {
                    // means chunk is completed now and can be released
                    chunk.Dispose();
                }
            }

            return value;
        }

        #region Unsafe Write \ CopyTo

        // write specified count of bytes from *psource
        internal void Write(byte* psource, int count)
        {
            if (count == 0) return;

            VerifyStreamInState(ChunkedStreamState.ReadWrite);

            while (count > 0)
            {
                int chunkOffset;
                var chunk = GetChunkForWrite(out chunkOffset);

                fixed (byte* ptarget = &chunk.Buffer[chunk.Offset + chunkOffset])
                {
                    int toWrite = Math.Min(count, _chunkSize - chunkOffset);
                    Buffer.MemoryCopy(psource, ptarget, toWrite, toWrite);

                    _position = checked(_position + toWrite);
                    psource += toWrite;
                    count -= toWrite;
                }
            }
            _length = Math.Max(_length, _position);
        }

        // copy whole stream into *ptarget
        internal void CopyTo(byte* ptarget)
        {
            if (_length == 0 || _chunks == null) return;

            VerifyStreamInState(ChunkedStreamState.ReadWrite);

            int chunkLength, lastChunkIndex = GetChunkIndexWithOffset(_length, out chunkLength);

            for (int i = 0; i < lastChunkIndex; i++)
            {
                ChunkCopy(_chunks[i], 0, _chunkSize, ptarget + (i << _chunkSizeShift));
            }

            ChunkCopy(_chunks[lastChunkIndex], 0, chunkLength, ptarget + (lastChunkIndex << _chunkSizeShift));
        }

        private void ChunkCopy(IChunk source, int start, int count, byte* ptarget)
        {
            if (source != null && count > 0)
            {
                fixed (byte* psource = &source.Buffer[source.Offset + start])
                {
                    Buffer.MemoryCopy(psource, ptarget, count, count);
                }
            }
        }

        #endregion

        // copy bytes from buffer provided
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count == 0) return;

            VerifyStreamInState(ChunkedStreamState.ReadWrite);
            VerifyInputBuffer(buffer, offset, count);

            while (count > 0)
            {
                int chunkOffset;
                var chunk = GetChunkForWrite(out chunkOffset);

                int toWrite = Math.Min(count, _chunkSize - chunkOffset);
                Buffer.BlockCopy(buffer, offset, chunk.Buffer, chunk.Offset + chunkOffset, toWrite);

                _position = checked(_position + toWrite);
                offset += toWrite;
                count -= toWrite;
            }
            _length = Math.Max(_length, _position);
        }

        // puts single byte on the stream
        public override void WriteByte(byte value)
        {
            VerifyStreamInState(ChunkedStreamState.ReadWrite);

            int chunkOffset;
            var chunk = GetChunkForWrite(out chunkOffset);
            chunk.Buffer[chunk.Offset + chunkOffset] = value;

            _length = Math.Max(_length, checked(++_position));
        }

        /// <summary>
        /// Moves current stream into ReadForward state. Means that only sequential read operations are allowed.
        /// Each chunk will be immediately released and put back into pool once it's processed.
        /// </summary>
        /// <param name="fromPosition">Initial poistion current stream will be read from.</param>
        public void AsOutputStream(int fromPosition = 0)
        {
            if (fromPosition < 0)
                throw new ArgumentOutOfRangeException("fromPosition", "fromPosition must be non-negative");

            VerifyStreamInState(ChunkedStreamState.ReadWrite);

            _position = fromPosition;
            _state = ChunkedStreamState.ReadForward;

            ReleaseLeft();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_state != ChunkedStreamState.Closed && disposing)
                {
                    if (_chunks != null)
                    {
                        for (int i = 0; i < _chunks.Length; i++)
                        {
                            _chunks[i]?.Dispose();
                            _chunks[i] = null;
                        }
                        _chunks = null;
                    }
                    _state = ChunkedStreamState.Closed;
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        #region Asserts

        internal static void VerifyInputBuffer(Array buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset", "offset must be non-negative");

            if (count < 0)
                throw new ArgumentOutOfRangeException("count", "count must be non-negative");

            if (buffer.Length - offset < count)
                throw new ArgumentException("offset + count must be less than buffer.Length");
        }

        private void VerifyStreamInState(ChunkedStreamState state)
        {
            if (state != _state)
            {
                throw new InvalidOperationException($"Current operation requires stream to be in {state} state. Current state is {_state}.");
            }
        }

        private void VerifyStreamNotInState(ChunkedStreamState state)
        {
            if (state == _state)
            {
                throw new InvalidOperationException($"Current operation is not allowed when stream is in {_state} state.");
            }
        }

        #endregion

        #region Internal Chunk Managemet

        // get chunk from pool or from managed heap, in case, when pool is empty or not available  
        private IChunk CreateChunk()
        {
            return _pool == null
                ? new MemoryChunk(_chunkSize)
                : _pool.GetChunk(); // pool will retrun chunk from the heap once it's empty
        }

        // returns null in case no data written yet - null is treated as an 0-valued byte array
        // returns chunk for current position otherwise
        private IChunk GetChunkForRead(out int offset)
        {
            int chunkIndex = GetChunkIndexWithOffset(_position, out offset);
            return _chunks?[chunkIndex];
        }

        // returns chunk for current position or creates new chunk if not allocated yet
        private IChunk GetChunkForWrite(out int offset)
        {
            int chunkIndex = GetChunkIndexWithOffset(_position, out offset);
            EnsureCapacity(chunkIndex);

            var chunk = _chunks[chunkIndex];
            if (chunk == null)
            {
                chunk = CreateChunk();
                _chunks[chunkIndex] = chunk;
            }

            return chunk;
        }

        // ensure _chunks[] are enough to hadnle specific index
        private void EnsureCapacity(int chunkIndex)
        {
            if (_chunks == null)
            {
                _chunks = new IChunk[Math.Max(DefaultChunksLength, chunkIndex + 1)];
            }
            else if (_chunks.Length <= chunkIndex)
            {
                var tmp = new IChunk[Math.Max(_chunks.Length << 1, chunkIndex + 1)];
                _chunks.CopyTo(tmp, 0);
                _chunks = tmp;
            }
        }

        // release all chunks allocated which are placed after the _length value
        private void ReleaseRight()
        {
            if (_chunks != null)
            {
                // if offset > 0 (so chunk is partially used) - skip current chunk or release otherwise
                int offset, chunkIndex = GetChunkIndexWithOffset(_length, out offset) + Math.Sign(offset);

                for (; chunkIndex < _chunks.Length; chunkIndex++)
                {
                    _chunks[chunkIndex]?.Dispose();
                    _chunks[chunkIndex] = null;
                }
            }
        }

        // release all chunks allocated which are placed before _position value
        private void ReleaseLeft()
        {
            if (_chunks != null)
            {
                int offset, chunkIndex = GetChunkIndexWithOffset(_position, out offset);

                for (int i = 0; i < chunkIndex; i++)
                {
                    _chunks[i]?.Dispose();
                    _chunks[i] = null;
                }
            }
        }

        // position = chunkIndex * chunkSize + offset
        private int GetChunkIndexWithOffset(long position, out int offset)
        {
            int chunkIndex = checked((int)(position >> _chunkSizeShift));
            offset = (int)(position & _chunkOffsetMask);
            return chunkIndex;
        }

        #endregion
    }
}
