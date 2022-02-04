namespace Ry.IO
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    public class ChunkPoolTests
    {
        [Fact]
        public void Holds_Under_Presure()
        {
            const int N = 1000;
            
            var pool = new ChunkPool(8, 1);

            Parallel.For(0, N, _ =>
            {
                bool updated = false;

                while (!updated)
                {
                    if (pool.TryRentFromPool(out var chunk))
                    {
                        var num = BitConverter.ToInt32(chunk.Data.Array, 4);
                        BitConverter.GetBytes(num + 1).CopyTo(chunk.Data.Array, 4);
                        pool.Return(ref chunk);
                        updated = true;
                    }
                }
            });

            var chunk_final = pool.Rent();

            Assert.True(chunk_final.IsFromPool);
            Assert.Equal(Chunk.InvalidHandle, BitConverter.ToInt32(chunk_final.Data.Array, 0));
            Assert.Equal(N, BitConverter.ToInt32(chunk_final.Data.Array, 4));
        }

        [Fact]
        public void Clears_Chunk_Buffer_When_Release()
        {
            var pool = new ChunkPool(8, 1);
            var chunk = pool.Rent();

            BitConverter.GetBytes(-1L).AsSpan().CopyTo(chunk.Data);
            pool.Return(ref chunk);

            Assert.True(chunk.IsNull);
        }

        [Fact]
        public void Clears_Chunk_Buffer_When_Acuire()
        {
            var pool = new ChunkPool(8, 1);
            var chunk = pool.Rent();

            BitConverter.GetBytes(-1L).AsSpan().CopyTo(chunk.Data);
            pool.Return(ref chunk);

            chunk = pool.Rent(clear: true);
            Assert.True(chunk.IsFromPool);
            Assert.Equal(new byte[8], chunk.Data.Array);
        }

        [Fact]
        public void Creates_Chunk_From_Pool()
        {
            var pool = new ChunkPool(8, 10);
            var chunks = new Chunk[10];

            for (int i = 0; i < chunks.Length; i++)
            {
                chunks[i] = pool.Rent();
                Assert.True(chunks[i].IsFromPool);
            }
        }

        [Fact]
        public void Creates_Chunk_From_Memory_When_Exhausted()
        {
            var pool = new ChunkPool(8, 1);

            var chunk = pool.Rent();
            Assert.True(chunk.IsFromPool);

            chunk = pool.Rent();
            Assert.True(chunk.IsFromMemory);
            Assert.Equal(new byte[8], chunk.Data.Array);
        }
    }
}