namespace ChunkedStream
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Buffers;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Diagnostics;

    public sealed class ChunkedStream : Stream
    {
        private const int DefaultChunkArraySize = 4;

        private readonly IChunkPool chunkPool;
        private readonly ArrayPool<Chunk> arrayPool;

        private Chunk[] chunks;
        private long length;
        private long position;

        public ChunkedStream(IChunkPool chunkPool)
            : this(chunkPool, ChunkArrayPool.Empty)
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

        public int ChunkSize => chunkPool.ChunkSize;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => length;

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
                    throw new ArgumentException();
                }

                position = value;
            }
        }

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

        public override void SetLength(long value)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            if (length < 0)
            {
                throw new Exception();
            }

            (var newIndex, var newOffset) = GetChunkPosition(value, upperBound: true);
            (var index, var offset) = GetChunkPosition(length, upperBound: true);

            if (newIndex == index)
            {
                if (newOffset > offset && index < chunks.Length)
                {
                    chunks[index].AsSpan(offset, newOffset - offset).Clear();
                }
            }
            else if (newIndex > index)
            {
                Debug.Assert(newIndex >= chunks.Length || chunks[newIndex].IsNull);

                if (offset < ChunkSize && index < chunks.Length)
                {
                    chunks[index].AsSpan(offset, ChunkSize - offset).Clear();
                }
            }
            else // newIndex < index
            {
                index = Math.Min(index, chunks.Length - 1);

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(new Span<byte>(buffer, offset, count));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new ReadOnlySpan<byte>(buffer, offset, count));
        }

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

        public void ForEach(Action<ArraySegment<byte>> action)
        {
            ForEach(0, length, action);
        }

        public void ForEach(long from, long to, Action<ArraySegment<byte>> action)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

            if (from < to)
            {
                IterateChunks(from, to, false, (chunk, offset, count) =>
                {
                    action(new ArraySegment<byte>(chunk, offset, count));
                });
            }
        }

        public Task ForEachAsync(Func<ArraySegment<byte>, Task> asyncAction)
        {
            return ForEachAsync(0, length, asyncAction);
        }

        public Task ForEachAsync(long from, long to, Func<ArraySegment<byte>, Task> asyncAction)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));

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

        public Task MoveToAsync(Stream target)
        {
            return MoveToAsync(target, CancellationToken.None);
        }

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

        public IBufferWriter<byte> GetWriter()
        {
            return GetWriter(ArrayPool<byte>.Shared);
        }

        public IBufferWriter<byte> GetWriter(ArrayPool<byte> pool)
        {
            if (chunks == null) throw new ObjectDisposedException(nameof(ChunkedStream));
            if (pool == null) throw new ArgumentNullException();

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
                    (var index, var _) = GetChunkPosition(length);

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
            Debug.Assert(!release || to == length); // Do not release in the middle of chunks.            

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

                action(chunks[i].Data.Array, chunks[i].Data.Offset + chunkFrom, chunkTo - chunkFrom);

                if (originalPos != position && originalLen != length)
                {
                    throw new Exception();
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
            Debug.Assert(!release || to == length); // Do not release in the middle of chunks.            

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

                await asyncAction(chunks[i].Data.Array, chunks[i].Data.Offset + chunkFrom, chunkTo - chunkFrom);

                if (originalPos != position && originalLen != length)
                {
                    throw new Exception();
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
                SetLength(position);
            }

            if (chunks[index].IsNull)
            {
                chunks[index] = chunkPool.Rent(clear: offset > 0 || length > position);
            }

            return (index, offset);
        }

        private (int, int) GetChunkPosition(long offset, bool upperBound = false)
        {
            var chunkIndex = (int)(offset / ChunkSize);
            var chunkOffset = (int)(offset % ChunkSize);

            if (upperBound && chunkOffset == 0)
            {
                return (chunkIndex - 1, ChunkSize);
            }

            return (chunkIndex, chunkOffset);
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
