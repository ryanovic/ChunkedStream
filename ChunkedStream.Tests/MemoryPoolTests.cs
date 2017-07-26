using System;
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
    public class MemoryPoolTests
    {
        [TestMethod]
        public void MemoryPool_ChunkSize()
        {
            var cases = new[] {
                new { ChunkSize = 1, ChunkCount = 1, Expected = 4 },
                new { ChunkSize = 3, ChunkCount = 1, Expected = 4 },
                new { ChunkSize = 4, ChunkCount = 1, Expected = 4 },
                new { ChunkSize = 5, ChunkCount = 1, Expected = 8 },
                new { ChunkSize = 510, ChunkCount = 1, Expected = 512 },
                new { ChunkSize = 1024, ChunkCount = 1, Expected = 1024 },
                new { ChunkSize = 4000, ChunkCount = 1, Expected = 4096 }};

            foreach (var @case in cases)
            {
                var memPool = new MemoryPool(chunkSize: @case.ChunkSize, chunkCount: @case.ChunkCount);
                Assert.AreEqual(@case.Expected, memPool.ChunkSize);
            }
        }

        [TestMethod]
        public void MemoryPool_ChunkCount()
        {
            var cases = Enumerable.Range(1, 10).Select(i => new { ChunkCount = i, ChunkSize = 4 });

            foreach (var @case in cases)
            {
                var memPool = new MemoryPool(chunkSize: @case.ChunkSize, chunkCount: @case.ChunkCount);

                for (int i = 0; i < @case.ChunkCount; i++)
                {
                    Assert.AreEqual(i, memPool.TryGetFreeChunkHandle());
                }
                Assert.AreEqual(-1, memPool.TryGetFreeChunkHandle());
            }
        }

        [TestMethod]
        public void MemoryPool_InvalidParameters()
        {
            var cases = new[] {
                new { ChunkSize = 0, ChunkCount = 1 },
                new { ChunkSize = -1, ChunkCount = 1},
                new { ChunkSize = 4, ChunkCount = 0},
                new { ChunkSize = 4, ChunkCount = -1},
                new { ChunkSize = 0, ChunkCount = 0},
                new { ChunkSize = -1, ChunkCount = -1}};

            foreach (var @case in cases)
            {
                try
                {
                    var memPool = new MemoryPool(chunkSize: @case.ChunkSize, chunkCount: @case.ChunkCount);
                    Assert.Fail("ArgumentException expected to be thrown");
                }
                catch (ArgumentException)
                {
                    // pass
                }
            }
        }

        [TestMethod]
        public void MemoryPool_GetChunk()
        {
            var cases = Enumerable.Range(1, 10).Select(i => new { ChunkCount = i, ChunkSize = 4 });

            foreach (var @case in cases)
            {
                var memPool = new MemoryPool(chunkSize: @case.ChunkSize, chunkCount: @case.ChunkCount);

                for (int i = 0; i < @case.ChunkCount; i++)
                {
                    Assert.IsInstanceOfType(memPool.GetChunk(), typeof(MemoryPoolChunk));
                }
                Assert.IsInstanceOfType(memPool.GetChunk(), typeof(MemoryChunk));
            }
        }

        [TestMethod]
        public void MemoryPool_TryGetChunkFromPool()
        {
            var cases = Enumerable.Range(1, 10).Select(i => new { ChunkCount = i, ChunkSize = 4 });

            foreach (var @case in cases)
            {
                var memPool = new MemoryPool(chunkSize: @case.ChunkSize, chunkCount: @case.ChunkCount);

                for (int i = 0; i < @case.ChunkCount; i++)
                {
                    Assert.IsInstanceOfType(memPool.TryGetChunkFromPool(), typeof(MemoryPoolChunk));
                }
                Assert.IsNull(memPool.TryGetChunkFromPool());
            }
        }

        [TestMethod]
        public void MemoryPool_PopPushPoolChunk()
        {
            var memPool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            int handle1 = memPool.TryGetFreeChunkHandle();
            int handle2 = memPool.TryGetFreeChunkHandle();

            Assert.AreEqual(0, handle1);
            Assert.AreEqual(1, handle2);
            Assert.AreEqual(-1, memPool.TryGetFreeChunkHandle());

            memPool.ReleaseChunkHandle(ref handle1);
            Assert.AreEqual(-1, handle1);
            Assert.AreEqual(0, memPool.TryGetFreeChunkHandle());
            Assert.AreEqual(-1, memPool.TryGetFreeChunkHandle());

            memPool.ReleaseChunkHandle(ref handle2);
            Assert.AreEqual(-1, handle2);
            Assert.AreEqual(1, memPool.TryGetFreeChunkHandle());
            Assert.AreEqual(-1, memPool.TryGetFreeChunkHandle());
        }

        [TestMethod]
        public void MemoryPool_ReleaseInvalidHandler()
        {
            var memPool = new MemoryPool(chunkSize: 4, chunkCount: 2);

            foreach (var i in new[] { -1, 2, 3, 100 })
            {
                int handle = i;
                try
                {
                    memPool.ReleaseChunkHandle(ref handle);
                    Assert.Fail("ArgumentException expected to be thrown");
                }
                catch (InvalidOperationException)
                {
                    // pass
                }
            }
        }

        [TestMethod]
        public void MemoryPool_AsParallel()
        {
            int threadCount = 8, attempts = 100, num = 100;

            var memPool = new MemoryPool(chunkSize: 4, chunkCount: threadCount / 2);

            var actions = Enumerable.Range(0, threadCount).Select(_ => new Action(() =>
            {
                for (int i = 0; i < attempts; i++)
                {
                    using (var chunk = memPool.GetChunk())
                    {
                        // initialize
                        BitConverter.GetBytes((int)0).CopyTo(chunk.Buffer, chunk.Offset);
                        for (int j = 0; j < num; j++)
                        {
                            // increment
                            BitConverter.GetBytes(BitConverter.ToInt32(chunk.Buffer, chunk.Offset) + 1).CopyTo(chunk.Buffer, chunk.Offset);
                        }
                        // assert
                        Assert.AreEqual(num, BitConverter.ToInt32(chunk.Buffer, chunk.Offset));
                    }
                }
            })).ToArray();

            Parallel.Invoke(actions);
        }
    }
}
