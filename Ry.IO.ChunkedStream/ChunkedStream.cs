namespace Ry.IO
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Buffers;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Diagnostics;

    /// <summary>
    /// Implements a stream class that uses a sequence of in-memory chunks to store data.
    /// </summary>
    public sealed class ChunkedStream : Stream
    {
        private const int DefaultChunkArraySize = 4;

        private readonly IChunkPool chunkPool;
        private readonly ArrayPool<Chunk> arrayPool;

        private Chunk[] chunks;
        private long length;
        private long position;

        public ChunkedStream(IChunkPool chunkPool)
            : this(chunkPool, EmptyChunkArrayPool.Instance)
        {
        }

        public ChunkedStream(IChunkPool chunkPool, ArrayPool<Chunk> arrayPool)
        {
            if (chunkPool == null) throw new ArgumentNullException(nameof(chunkPool));
            if (arrayPool == null) throw new ArgumentNullException(nameof(arrayPool));

            this.chunkPool = chunkPool;
            this.arrayPool = arrayPool;
            this.chunks = arrayPool.Rent(DefaultChunkArraySize);
        }

        /// <summary>
        /// Gets the chunks size defined for the current stream.
        /// </summary>
        public int ChunkSize => chunkPool.ChunkSize;

        /// <summary>
        /// Gets a value indicating whether the current stream supports reading.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking.
        /// </summary>
        public override bool CanSeek => true;

        /// <summary>
        /// Gets a value indicating whether the current stream supports writing.
        /// </summary>
        public override bool CanWrite => true;

        /// <summary>
        /// Gets the length in bytes of the stream.
        /// </summary>
        public override long Length => length;

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        public override long Position
        {
            get
            {
                return position;
            }
            set
            {
                if (chunks == null)
                {
                    throw new ObjectDisposedException(nameof(ChunkedStream));
                }

                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), Errors.NegativeNumber("Position"));
                }

                position = value;
            }
        }

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position = position + offset;
                    break;
                case SeekOrigin.End:
                    Position = length + offset;
                    break;
            }

            return position;
        }

        /// <summary>
        /// Sets the length of the current stream
        /// </summary>
        public override void SetLength(long value)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(value), Errors.NegativeNumber("Length"));

            (var newIndex, var newOffset) = GetChunkPosition(value, upperBound: true);
            (var index, var offset) = GetChunkPosition(length, upperBound: true);

            if (newIndex == index)
            {
                if (newOffset > offset && index < chunks.Length)
                {
                    // Clear [size .. new-size] range.
                    chunks[index].AsSpan(offset, newOffset - offset).Clear();
                }
            }
            else if (newIndex > index)
            {
                Debug.Assert(newIndex >= chunks.Length || chunks[newIndex].IsNull);

                // New length is pointing to some uninitialized chunk(by design).
                // Clear [size ...].
                if (offset < ChunkSize && index < chunks.Length)
                {
                    chunks[index].AsSpan(offset, ChunkSize - offset).Clear();
                }
            }
            else // newIndex < index
            {
                index = Math.Min(index, chunks.Length - 1);

                // Release chunks right to new length.
                for (int i = newIndex + 1; i <= index; i++)
                {
                    Return(ref chunks[i]);
                }
            }

            position = Math.Min(position, length = value);
        }

        public override int ReadByte()
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            if (position < length)
            {
                (var index, var offset) = GetChunkPositionToRead();
                var chunk = chunks[index];

                position++;

                return chunk.IsNull ? 0 : chunk.Span[offset];
            }

            return -1;
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(new Span<byte>(buffer, offset, count));
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
#if NETSTANDARD2_0
        public int Read(Span<byte> buffer)
#else
        public override int Read(Span<byte> buffer)
#endif
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            var totalRead = 0;

            while (buffer.Length > 0 && position < length)
            {
                (var index, var offset) = GetChunkPositionToRead();

                var toRead = (int)Math.Min(length - position, chunkPool.ChunkSize - offset);
                toRead = Math.Min(toRead, buffer.Length);

                Debug.Assert(toRead > 0);

                if (chunks[index].IsNull)
                {
                    buffer.Slice(0, toRead).Clear();
                }
                else
                {
                    chunks[index].AsSpan(offset, toRead).CopyTo(buffer);
                }

                position += toRead;
                totalRead += toRead;
                buffer = buffer.Slice(toRead);

                Debug.Assert(position <= length);
            }

            return totalRead;
        }

        public override void WriteByte(byte value)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            (var index, var offset) = GetChunkPositionToWrite();
            chunks[index].Span[offset] = value;
            length = Math.Max(length, ++position);
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new ReadOnlySpan<byte>(buffer, offset, count));
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
#if NETSTANDARD2_0
        public void Write(ReadOnlySpan<byte> buffer)
#else
        public override void Write(ReadOnlySpan<byte> buffer)
#endif
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            while (buffer.Length > 0)
            {
                (var index, var offset) = GetChunkPositionToWrite();

                var target = chunks[index].Data.AsSpan(offset);
                var toWrite = Math.Min(buffer.Length, target.Length);

                Debug.Assert(toWrite > 0);

                buffer.Slice(0, toWrite).CopyTo(target);
                buffer = buffer.Slice(toWrite);
                position += toWrite;
                length = Math.Max(length, position);

                Debug.Assert(position <= length);
            }
        }

        /// <summary>
        /// Copies the entire stream to an array. Does not affect he current position.
        /// </summary>
        public byte[] ToArray()
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[length];
            var temp = position;

            position = 0;
            Read(buffer);
            position = temp;

            return buffer;
        }

        /// <summary>
        /// Executes <paramref name="action"/> for each chunk in the current stream. Does not affect he current position.
        /// </summary>
        public void ForEach(Action<ArraySegment<byte>> action)
        {
            ForEach(0, length, action);
        }

        /// <summary>
        /// Executes <paramref name="action"/> for each chunk in the specified range. Does not affect he current position.
        /// </summary>
        /// <param name="from">The inclusive position in the stream to start from.</param>
        /// <param name="to">The exclusive position in the stream where to end.</param>
        /// <param name="action">User defined hander.</param>
        public void ForEach(long from, long to, Action<ArraySegment<byte>> action)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));
            if (from < 0) throw new ArgumentOutOfRangeException(nameof(from));
            if (to > length) throw new ArgumentOutOfRangeException(nameof(to));
            if (from > to) throw new InvalidOperationException(Errors.ReversedOrder());

            if (from < to)
            {
                IterateChunks(from, to, false, (chunk, offset, count) =>
                {
                    action(new ArraySegment<byte>(chunk, offset, count));
                });
            }
        }

        /// <summary>
        /// Executes <paramref name="asyncAction"/> for each chunk in the current stream. Does not affect he current position.
        /// </summary>
        public Task ForEachAsync(Func<ArraySegment<byte>, Task> asyncAction)
        {
            return ForEachAsync(0, length, asyncAction);
        }

        /// <summary>
        /// Executes <paramref name="asyncAction"/> for each chunk in the specified range. Does not affect he current position.
        /// </summary>
        /// <param name="from">The inclusive position in the stream to start from.</param>
        /// <param name="to">The exclusive position in the stream where to end.</param>
        /// <param name="asyncAction">User defined hander.</param>
        public Task ForEachAsync(long from, long to, Func<ArraySegment<byte>, Task> asyncAction)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));
            if (from < 0) throw new ArgumentOutOfRangeException(nameof(from));
            if (to > length) throw new ArgumentOutOfRangeException(nameof(to));
            if (from > to) throw new InvalidOperationException(Errors.ReversedOrder());

            if (from < to)
            {
                var task = IterateChunksAsync(from, to, false, (chunk, offset, count) =>
                {
                    return asyncAction(new ArraySegment<byte>(chunk, offset, count));
                });

                return task;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Writes all bytes from the current position to another stream then removes them from the current stream.
        /// </summary>
        public void MoveTo(Stream target)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            if (position < length)
            {
                IterateChunks(position, length, true, (chunk, offset, count) =>
                {
                    target.Write(chunk, offset, count);
                });

                length = position;
            }
        }

        /// <summary>
        /// Writes all bytes from the current position to another stream then removes them from the current stream.
        /// </summary>
        public Task MoveToAsync(Stream target)
        {
            return MoveToAsync(target, CancellationToken.None);
        }

        /// <summary>
        /// Writes all bytes from the current position to another stream then removes them from the current stream.
        /// </summary>
        public Task MoveToAsync(Stream target, CancellationToken cancellationToken)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            if (position < length)
            {
                var task = IterateChunksAsync(position, length, true, (chunk, offset, count) =>
                {
                    return target.WriteAsync(chunk, offset, count, cancellationToken);
                });

                return task.ContinueWith(_ => length = position);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a <see cref="IBufferWriter{T}"/> for the current stream.
        /// </summary>
        public IBufferWriter<byte> GetWriter()
        {
            return GetWriter(ArrayPool<byte>.Shared);
        }

        /// <summary>
        /// Creates a <see cref="IBufferWriter{T}"/> for the current stream.
        /// </summary>
        public IBufferWriter<byte> GetWriter(ArrayPool<byte> pool)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            return new BufferWriter(this, pool);
        }

        public override void Flush()
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && chunks != null)
                {
                    // It could be one chunk allocated beyond the length if an exception occurs during the write, hence upperBound is FALSE.
                    (var index, var _) = GetChunkPosition(length, upperBound: false);

                    index = Math.Min(index, chunks.Length - 1);

                    while (index >= 0)
                    {
                        Return(ref chunks[index--]);
                    }

                    Debug.Assert(chunks.All(x => x.IsNull));

                    arrayPool.Return(chunks);
                    position = length = 0;
                    chunks = null;
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        private void IterateChunks(long from, long to, bool release, Action<byte[], int, int> action)
        {
            Debug.Assert(from >= 0 && from < to && to <= length);
            Debug.Assert(!release || to == length); // Do not release in the middle.

            (var lo, var loOffset) = GetChunkPosition(from);
            (var hi, var hiOffset) = GetChunkPosition(to, upperBound: true);

            var originalPos = position;
            var originalLen = length;

            EnsureCapacity(hi);

            for (int i = lo; i <= hi; i++)
            {
                var chunkFrom = i == lo ? loOffset : 0;
                var chunkTo = i == hi ? hiOffset : ChunkSize;

                if (chunks[i].IsNull)
                {
                    chunks[i] = chunkPool.Rent(clear: true);
                }

                Debug.Assert(chunkFrom < chunkTo);

                action(chunks[i].Data.Array, chunks[i].Data.Offset + chunkFrom, chunkTo - chunkFrom);

                if (originalPos != position || originalLen != length)
                {
                    throw new InvalidOperationException(Errors.StreamIsChanged());
                }

                if (release && (i > lo || chunkFrom == 0))
                {
                    chunkPool.Return(ref chunks[i]);
                }
            }
        }

        private async Task IterateChunksAsync(long from, long to, bool release, Func<byte[], int, int, Task> asyncAction)
        {
            Debug.Assert(from >= 0 && from < to && to <= length);
            Debug.Assert(!release || to == length); // Do not release in the middle.

            (var lo, var loOffset) = GetChunkPosition(from);
            (var hi, var hiOffset) = GetChunkPosition(to, upperBound: true);

            var originalPos = position;
            var originalLen = length;

            EnsureCapacity(hi);

            for (int i = lo; i <= hi; i++)
            {
                var chunkFrom = i == lo ? loOffset : 0;
                var chunkTo = i == hi ? hiOffset : ChunkSize;

                if (chunks[i].IsNull)
                {
                    chunks[i] = chunkPool.Rent(clear: true);
                }

                Debug.Assert(chunkFrom < chunkTo);

                await asyncAction(chunks[i].Data.Array, chunks[i].Data.Offset + chunkFrom, chunkTo - chunkFrom);

                if (originalPos != position || originalLen != length)
                {
                    throw new InvalidOperationException(Errors.StreamIsChanged());
                }

                if (release && (i > lo || chunkFrom == 0))
                {
                    chunkPool.Return(ref chunks[i]);
                }
            }
        }

        private (int, int) GetChunkPositionToRead()
        {
            (var index, var offset) = GetChunkPosition(position);
            EnsureCapacity(index);
            return (index, offset);
        }

        private (int, int) GetChunkPositionToWrite()
        {
            (var index, var offset) = GetChunkPosition(position);

            EnsureCapacity(index);

            if (position > length)
            {
                // Reset [length .. position) bytes.
                SetLength(position);
            }

            if (chunks[index].IsNull)
            {
                // Zero a chunk if it's allocating in the middle of the stream.
                chunks[index] = chunkPool.Rent(clear: offset > 0 || length > position);
            }

            return (index, offset);
        }

        private (int, int) GetChunkPosition(long offset, bool upperBound = false)
        {
            var chunkIndex = offset / ChunkSize;
            var chunkOffset = (int)(offset % ChunkSize);

            if (chunkIndex > Int32.MaxValue)
            {
                throw new InvalidOperationException(Errors.StreamMaxSize());
            }

            if (upperBound && chunkOffset == 0)
            {
                // For upper bound return the end of the previous chunk.
                return ((int)chunkIndex - 1, ChunkSize);
            }

            return ((int)chunkIndex, chunkOffset);
        }

        private void EnsureCapacity(int index)
        {
            if (index >= chunks.Length)
            {
                var temp = arrayPool.Rent(index + 1);

                Debug.Assert(temp.All(x => x.IsNull));

                chunks.CopyTo(temp, 0);
                arrayPool.Return(chunks, clearArray: true);
                chunks = temp;
            }
        }

        private void Return(ref Chunk chunk)
        {
            if (!chunk.IsNull)
            {
                chunkPool.Return(ref chunk);
            }
        }

        private sealed class BufferWriter : IBufferWriter<byte>
        {
            private readonly ArrayPool<byte> pool;
            private readonly ChunkedStream stream;
            private byte[] tempBuffer;

            public BufferWriter(ChunkedStream stream, ArrayPool<byte> pool)
            {
                this.pool = pool;
                this.stream = stream;
            }

            public void Advance(int count)
            {
                if (tempBuffer == null)
                {
                    stream.position += count;
                    stream.length = Math.Max(stream.length, stream.position);
                }
                else
                {
                    stream.Write(tempBuffer, 0, count);
                    pool.Return(tempBuffer);
                    tempBuffer = null;
                }
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                (var index, var offset) = stream.GetChunkPositionToWrite();

                if (sizeHint == 0 || sizeHint <= (stream.chunkPool.ChunkSize - offset))
                {
                    return stream.chunks[index].Data.AsMemory(offset);
                }

                tempBuffer = pool.Rent(sizeHint);
                return new Memory<byte>(tempBuffer);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                return GetMemory(sizeHint).Span;
            }
        }
    }
}