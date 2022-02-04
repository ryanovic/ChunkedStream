namespace Ry.IO
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    public class ChunkedStreamTests
    {
        [Fact]
        public void Writes_And_Reads_Bytes()
        {
            var source = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var pool = new TestPool(chunkSize: 1);

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(source);

                var buffer = new byte[stream.Length];

                stream.Position = 0;
                stream.Read(buffer);

                Assert.Equal(stream.Length, stream.Position);
                Assert.Equal(10, pool.Allocated);
                Assert.Equal(source, buffer);
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Fact]
        public void Writes_And_Reads_Bytes_In_Parts()
        {
            var source = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var pool = new TestPool(chunkSize: 3);

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(source, 0, 2);
                stream.Write(source, 2, 2);
                stream.Write(source, 4, 2);
                stream.Write(source, 6, 2);
                stream.Write(source, 8, 2);

                var buffer = new byte[stream.Length];

                stream.Position = 0;
                stream.Read(buffer, 0, 2);
                stream.Read(buffer, 2, 2);
                stream.Read(buffer, 4, 2);
                stream.Read(buffer, 6, 2);
                stream.Read(buffer, 8, 2);

                Assert.Equal(stream.Length, stream.Position);
                Assert.Equal(4, pool.Allocated);
                Assert.Equal(source, buffer);
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Fact]
        public void Writes_And_Reads_Bytes_One_By_One()
        {
            var source = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var pool = new TestPool(chunkSize: 2);

            using (var stream = new ChunkedStream(pool))
            {
                for (int i = 0; i < source.Length; i++)
                {
                    stream.WriteByte(source[i]);
                }

                var buffer = new byte[stream.Length];

                stream.Position = 0;

                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = (byte)stream.ReadByte();
                }

                Assert.Equal(-1, stream.ReadByte());
                Assert.Equal(stream.Length, stream.Position);
                Assert.Equal(5, pool.Allocated);
                Assert.Equal(source, buffer);
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Fact]
        public void SetLength_Creates_Empty_Stream()
        {
            var pool = new TestPool(chunkSize: 1);

            using (var stream = new ChunkedStream(pool))
            {
                stream.SetLength(5);

                Assert.Equal(new byte[5], stream.ToArray());
                Assert.Equal(0, stream.Position);
                Assert.Equal(5, stream.Length);
                Assert.Equal(0, pool.Allocated);
            }
        }

        [Theory]
        [InlineData(4, new byte[] { 0, 1, 2, 3 })]
        [InlineData(0, new byte[] { })]
        [InlineData(6, new byte[] { 0, 1, 2, 3, 4, 0 })]
        [InlineData(10, new byte[] { 0, 1, 2, 3, 4, 0, 0, 0, 0, 0 })]
        public void SetLength_Updates_Stream(int newLength, byte[] expected)
        {
            var pool = new TestPool(chunkSize: 3);
            var buffer = new byte[] { 0, 1, 2, 3, 4 };

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(buffer);
                stream.SetLength(newLength);

                Assert.Equal(expected, stream.ToArray());
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Theory]
        [InlineData(0, SeekOrigin.Begin, new byte[] { 99, 1, 2 })]
        [InlineData(1, SeekOrigin.Current, new byte[] { 0, 1, 2, 0, 99 })]
        [InlineData(-1, SeekOrigin.End, new byte[] { 0, 1, 99 })]
        public void Writes_Byte_At_Position(int offset, SeekOrigin origin, byte[] expected)
        {
            var pool = new TestPool(chunkSize: 1);
            var buffer = new byte[] { 0, 1, 2 };

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(buffer);
                stream.Seek(offset, origin);
                stream.WriteByte(99);

                Assert.Equal(expected, stream.ToArray());
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Theory]
        [InlineData(0, new byte[] { 99, 99, 0, 0 })]
        [InlineData(3, new byte[] { 0, 0, 0, 99, 99 })]
        [InlineData(6, new byte[] { 0, 0, 0, 0, 0, 0, 99, 99 })]
        [InlineData(12, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 99, 99 })]
        public void Writes_Byte_At_Position_Of_Empty_Stream(int position, byte[] expected)
        {
            var pool = new TestPool(chunkSize: 2);
            var buffer = new byte[] { 99, 99 };

            using (var stream = new ChunkedStream(pool))
            {
                stream.SetLength(4);
                stream.Position = position;
                stream.Write(buffer);

                Assert.Equal(expected, stream.ToArray());
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Theory]
        [InlineData(0, 6, new byte[] { 0, 1, 2, 3, 4, 5 })]
        [InlineData(1, 5, new byte[] { 1, 2, 3, 4 })]
        public void Iterates_Chunks(int from, int to, byte[] expected)
        {
            var pool = new TestPool(chunkSize: 2);
            var source = new byte[] { 0, 1, 2, 3, 4, 5 };
            var buffer = new byte[expected.Length];
            var offset = 0;

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(source);

                stream.ForEach(from, to, chunk =>
                {
                    Buffer.BlockCopy(chunk.Array, chunk.Offset, buffer, offset, chunk.Count);
                    offset += chunk.Count;
                });

                Assert.Equal(expected, buffer);
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Theory]
        [InlineData(0, 6, new byte[] { 0, 1, 2, 3, 4, 5 })]
        [InlineData(1, 5, new byte[] { 1, 2, 3, 4 })]
        public async Task Iterates_Chunks_Async(int from, int to, byte[] expected)
        {
            var pool = new TestPool(chunkSize: 2);
            var source = new byte[] { 0, 1, 2, 3, 4, 5 };
            var buffer = new byte[expected.Length];
            var offset = 0;

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(source);

                await stream.ForEachAsync(from, to, async chunk =>
                {
                    await Task.Delay(0);
                    Buffer.BlockCopy(chunk.Array, chunk.Offset, buffer, offset, chunk.Count);
                    offset += chunk.Count;
                });

                Assert.Equal(expected, buffer);
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(6)]
        public void Moves_Chunks(int from)
        {
            var pool = new TestPool(chunkSize: 2);
            var source = new byte[] { 0, 1, 2, 3, 4, 5 };
            var expected = new byte[source.Length - from];
            var memStream = new MemoryStream();
            Buffer.BlockCopy(source, from, expected, 0, expected.Length);

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(source);
                stream.Position = from;
                stream.MoveTo(memStream);

                Assert.Equal(expected, memStream.ToArray());
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(6)]
        public async Task Moves_Chunks_Async(int from)
        {
            var pool = new TestPool(chunkSize: 2);
            var source = new byte[] { 0, 1, 2, 3, 4, 5 };
            var expected = new byte[source.Length - from];
            var memStream = new MemoryStream();
            Buffer.BlockCopy(source, from, expected, 0, expected.Length);

            using (var stream = new ChunkedStream(pool))
            {
                stream.Write(source);
                stream.Position = from;
                await stream.MoveToAsync(memStream);

                Assert.Equal(expected, memStream.ToArray());
            }

            Assert.Equal(0, pool.Allocated);
        }

        [Fact]
        public void BufferWriter_Writes_In_Stream()
        {
            var sourceBuffer = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            var source = sourceBuffer.AsSpan();
            var pool = new TestPool(chunkSize: 3);

            using (var stream = new ChunkedStream(pool))
            {
                var writer = stream.GetWriter();

                var span = writer.GetSpan(2);
                source.Slice(0, 2).CopyTo(span);
                writer.Advance(2);

                span = writer.GetSpan(2);
                source.Slice(2, 2).CopyTo(span);
                writer.Advance(2);

                span = writer.GetSpan(2);
                source.Slice(4, 2).CopyTo(span);
                writer.Advance(2);

                span = writer.GetSpan(0);
                source.Slice(6, 2).CopyTo(span);
                writer.Advance(2);

                Assert.Equal(sourceBuffer, stream.ToArray());
            }

            Assert.Equal(0, pool.Allocated);
        }
    }
}