using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ChunkedStream;
using ChunkedStream.Chunks;

namespace ChunkedStream.Tests
{
    [TestClass]
    public class ChunkedStreamTests
    {
        [TestMethod]
        public void ChunkedStream_FromPool_Throws_WhenNoPool()
        {
            try
            {
                var stream = ChunkedStream.FromPool();
                Assert.Fail("ArgumentNullException is expected");
            }
            catch (ArgumentNullException)
            {
            }
        }

        [TestMethod]
        public void ChunkedStream_ReadWriteByte()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);
            var stream = ChunkedStream.FromPool(pool);

            for (int i = 0; i < 16; i++)
            {
                stream.WriteByte((byte)i);
            }

            Assert.AreEqual(2, pool.TotalAllocated);
            Assert.AreEqual(16, stream.Position);

            stream.Position = 0;

            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(i, stream.ReadByte());
            }

            stream.Dispose();
            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStream_ReadWriteByte_FromMemory()
        {
            using (var stream = ChunkedStream.FromMemory(memoryChunkSize: 4))
            {
                for (int i = 0; i < 16; i++)
                {
                    stream.WriteByte((byte)i);
                }

                Assert.AreEqual(16, stream.Position);

                stream.Position = 0;

                for (int i = 0; i < 16; i++)
                {
                    Assert.AreEqual(i, stream.ReadByte());
                }
            }
        }

        [TestMethod]
        public void ChunkedStream_ReadWrite()
        {
            byte[] buffer = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);
            var stream = ChunkedStream.FromPool(pool);

            stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(16, stream.Length);
            Assert.AreEqual(2, pool.TotalAllocated);

            stream.Position = 0;
            stream.Read(buffer, 0, buffer.Length);

            Assert.AreEqual(16, stream.Position);
            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.AreEqual(i, buffer[i]);
            }

            stream.Dispose();
            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStream_ReadWrite_FromMemory()
        {
            byte[] buffer = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

            using (var stream = ChunkedStream.FromMemory(memoryChunkSize: 4))
            {
                stream.Write(buffer, 0, buffer.Length);

                Assert.AreEqual(16, stream.Length);

                stream.Position = 0;
                stream.Read(buffer, 0, buffer.Length);

                Assert.AreEqual(16, stream.Position);
                for (int i = 0; i < buffer.Length; i++)
                {
                    Assert.AreEqual(i, buffer[i]);
                }
            }
        }

        [TestMethod]
        public void ChunkedStream_ReadWrite_WhenManyCalls()
        {
            foreach (int count in Enumerable.Range(1, 32))
            {
                byte[] buffer = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();

                var pool = new MemoryPool(chunkSize: 16, chunkCount: 32);
                var stream = ChunkedStream.FromPool(pool);

                for (int i = 0; i < buffer.Length; i += count)
                {
                    stream.Write(buffer, i, Math.Min(count, buffer.Length - i));
                }

                Assert.AreEqual(1024, stream.Length);
                Assert.AreEqual(32, pool.TotalAllocated);

                stream.Position = 0;
                Array.Clear(buffer, 0, buffer.Length);

                for (int i = 0; i < buffer.Length; i += count)
                {
                    stream.Read(buffer, i, Math.Min(count, buffer.Length - i));
                }

                Assert.AreEqual(1024, stream.Position);


                for (int i = 0; i < buffer.Length; i++)
                {
                    Assert.AreEqual((byte)i, buffer[i], $"Failed on {count} buffer length");
                }

                stream.Dispose();
                Assert.AreEqual(0, pool.TotalAllocated);
            }
        }

        [TestMethod]
        public void ChunkedStream_ReadWrite_WhenGlobalPool_InParallel()
        {
            try
            {
                int threadCount = 8;
                ChunkedStream.InitializePool(64, 32);

                var actions = Enumerable.Range(0, threadCount).Select(_ => new Action(() =>
                {
                    foreach (int count in Enumerable.Range(1, 32))
                    {
                        byte[] buffer = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
                        using (var stream = new ChunkedStream())
                        {
                            for (int i = 0; i < buffer.Length; i += count)
                            {
                                stream.Write(buffer, i, Math.Min(count, buffer.Length - i));
                            }

                            Assert.AreEqual(1024, stream.Length);

                            stream.Position = 0;
                            Array.Clear(buffer, 0, buffer.Length);

                            for (int i = 0; i < buffer.Length; i += count)
                            {
                                stream.Read(buffer, i, Math.Min(count, buffer.Length - i));
                            }

                            Assert.AreEqual(1024, stream.Position);


                            for (int i = 0; i < buffer.Length; i++)
                            {
                                Assert.AreEqual((byte)i, buffer[i], $"Failed on {count} buffer length");
                            }
                        }
                    }
                })).ToArray();

                Parallel.Invoke(actions);
            }
            finally
            {
                ChunkedStream.DisablePool();
            }
        }

        [TestMethod]
        public void ChunkedStream_ReadByte_ByPosition()
        {
            byte[] buffer = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                stream.Write(buffer, 0, 16);

                var cases = new[] {
                    new { Position = 0, Expected = 0 },
                    new { Position = 4, Expected = 4 },
                    new { Position = 15, Expected = 15 },
                    new { Position = 16, Expected = -1 },
                    new { Position = 32, Expected = -1 },
                    new { Position = 1024, Expected = -1 }
                };

                foreach (var @case in cases)
                {
                    stream.Position = @case.Position;
                    Assert.AreEqual(@case.Position, stream.Position);
                    Assert.AreEqual(@case.Expected, stream.ReadByte());
                }

            }
            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStream_WriteByte_ByPosition()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                var cases = new[] {
                    new { Position = 0, Expected = 2 },
                    new { Position = 4, Expected = 4 },
                    new { Position = 15, Expected = 8 },
                    new { Position = 16, Expected = 16 },
                    new { Position = 32, Expected = 32 },
                    new { Position = 1024, Expected = 64 }
                };

                foreach (var @case in cases)
                {
                    stream.Position = @case.Position;
                    stream.WriteByte((byte)@case.Expected);

                    Assert.AreEqual(@case.Position + 1, stream.Length);
                    Assert.AreEqual(@case.Position + 1, stream.Position);

                    stream.Position = @case.Position;
                    Assert.AreEqual(@case.Expected, stream.ReadByte());
                }

            }
            Assert.AreEqual(0, pool.TotalAllocated);
        }

        [TestMethod]
        public void ChunkedStream_Seek()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                stream.SetLength(16);

                Assert.AreEqual(16, stream.Length);
                Assert.AreEqual(0, stream.Position);

                var cases = new[] {
                    new { Expected = 0, Position = 0, Origin = SeekOrigin.Begin },
                    new { Expected = 5, Position = 5, Origin = SeekOrigin.Begin },
                    new { Expected = 0, Position = -5, Origin = SeekOrigin.Current },
                    new { Expected = 15, Position = 15, Origin = SeekOrigin.Current },
                    new { Expected = 0, Position = -16, Origin = SeekOrigin.End },
                    new { Expected = 6, Position = -10, Origin = SeekOrigin.End },
                    new { Expected = 1016, Position = 1000, Origin = SeekOrigin.End }
                };

                foreach (var @case in @cases)
                {
                    stream.Seek(@case.Position, @case.Origin);

                    Assert.AreEqual(16, stream.Length);
                    Assert.AreEqual(@case.Expected, stream.Position);
                }

            }
        }

        [TestMethod]
        public void ChunkedStream_Position()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                stream.SetLength(16);

                Assert.AreEqual(16, stream.Length);
                Assert.AreEqual(0, stream.Position);

                var cases = new[] {
                    new { Position = 0 },
                    new { Position = 5 },
                    new { Position = 0 },
                    new { Position = 15 },
                    new { Position = 0},
                    new { Position = 6},
                    new { Position = 1016}
                };

                foreach (var @case in @cases)
                {
                    stream.Position = @case.Position;

                    Assert.AreEqual(16, stream.Length);
                    Assert.AreEqual(@case.Position, stream.Position);
                }

            }
        }

        [TestMethod]
        public void ChunkedStream_SetLength()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                Assert.AreEqual(0, stream.Length);
                Assert.AreEqual(0, stream.Position);

                stream.SetLength(16);

                Assert.AreEqual(0, pool.TotalAllocated);
                Assert.AreEqual(16, stream.Length);
                Assert.AreEqual(0, stream.Position);

                stream.Position = 32;
                stream.SetLength(64);

                Assert.AreEqual(0, pool.TotalAllocated);
                Assert.AreEqual(64, stream.Length);
                Assert.AreEqual(32, stream.Position);

                stream.SetLength(16);

                Assert.AreEqual(0, pool.TotalAllocated);
                Assert.AreEqual(16, stream.Length);
                Assert.AreEqual(16, stream.Position);
            }
        }

        [TestMethod]
        public void ChunkedStream_SetLength_WhenLessThanPosition()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                stream.Position = 4;
                stream.WriteByte(1);

                Assert.AreEqual(1, pool.TotalAllocated);
                Assert.AreEqual(5, stream.Length);

                stream.SetLength(4);
                Assert.AreEqual(0, pool.TotalAllocated);
                Assert.AreEqual(4, stream.Length);
            }
        }

        [TestMethod]
        public void ChunkedStream_Read()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                byte[] buffer = new byte[4] { 255, 255, 255, 255 };

                stream.SetLength(8);
                Assert.AreEqual(4, stream.Read(buffer, 0, buffer.Length));

                stream.Position += 2;
                Assert.AreEqual(2, stream.Read(buffer, 0, buffer.Length));

                Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));

                stream.Position = 1000;
                Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
            }
        }

        [TestMethod]
        public void ChunkedStream_Write()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                byte[] buffer = new byte[4] { 255, 255, 255, 255 };

                stream.Position = 4;
                stream.Write(buffer, 0, buffer.Length);
                Assert.AreEqual(8, stream.Length);

                stream.Position = 16;
                stream.Write(buffer, 0, buffer.Length);
                Assert.AreEqual(20, stream.Length);

                stream.Position = 0;
                Assert.AreEqual(0, stream.ReadByte());

                stream.Position = 3;
                Assert.AreEqual(0, stream.ReadByte());

                stream.Position = 4;
                Assert.AreEqual(255, stream.ReadByte());

                stream.Position = 15;
                Assert.AreEqual(0, stream.ReadByte());

                stream.Position = 16;
                Assert.AreEqual(255, stream.ReadByte());

                stream.Position = 1000;
                Assert.AreEqual(-1, stream.ReadByte());
            }
        }

        [TestMethod]
        public void ChunkedStream_AsOutputStream()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 4);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                byte[] buffer = new byte[4] { 255, 255, 255, 255 };

                for (int i = 0; i < 8; i++)
                {
                    stream.Position = i * 4;

                    if (i % 2 == 0)
                    {
                        stream.Write(buffer, 0, buffer.Length);
                    }
                }
                Assert.AreEqual(4, pool.TotalAllocated);

                // skip 1 full chunk
                stream.AsOutputStream(fromPosition: 4);
                Assert.AreEqual(3, pool.TotalAllocated);

                // read 2 null chunk by byte
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(0, stream.ReadByte());
                }
                Assert.AreEqual(3, pool.TotalAllocated);

                // read 3 full chunk by byte
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(255, stream.ReadByte());
                }
                Assert.AreEqual(2, pool.TotalAllocated);

                // skip 4, 5 chunks by seek
                stream.Seek(8, SeekOrigin.Current);
                Assert.AreEqual(1, pool.TotalAllocated);

                // read 6 null chunk
                Array.Clear(buffer, 0, buffer.Length);
                stream.Read(buffer, 0, buffer.Length);

                Assert.AreEqual(0, buffer.Select(x => (int)x).Sum());
                Assert.AreEqual(1, pool.TotalAllocated);

                // read 7 full chunk
                Array.Clear(buffer, 0, buffer.Length);
                stream.Read(buffer, 0, buffer.Length);

                Assert.AreEqual(1020, buffer.Select(x => (int)x).Sum());
                Assert.AreEqual(0, pool.TotalAllocated);

                // complete stream
                Assert.AreEqual(-1, stream.ReadByte());
                Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));

                Assert.AreEqual(28, stream.Length);
                Assert.AreEqual(28, stream.Position);
            }
        }

        [TestMethod]
        public void ChunkedStream_AsOutputStream_WhenDefault()
        {
            var pool = new MemoryPool(chunkSize: 4, chunkCount: 4);

            using (var stream = ChunkedStream.FromPool(pool))
            {
                byte[] buffer = new byte[4] { 255, 255, 255, 255 };
                stream.Write(buffer, 0, buffer.Length);

                stream.AsOutputStream();
                Assert.AreEqual(1, pool.TotalAllocated);
                Assert.AreEqual(0, stream.Position);

                Array.Clear(buffer, 0, buffer.Length);
                
                Assert.AreEqual(4, stream.Read(buffer, 0, buffer.Length));
                Assert.AreEqual(1020, buffer.Select(x => (int)x).Sum());
                Assert.AreEqual(0, pool.TotalAllocated);
            }
        }
    }
}
