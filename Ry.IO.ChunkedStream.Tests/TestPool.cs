namespace Ry.IO
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Xunit;

    public class TestPool : IChunkPool
    {
        public int ChunkSize { get; set; }

        public int Available;
        public int Allocated;
        public int Total => Available + Allocated;

        public TestPool(int chunkSize)
        {
            this.ChunkSize = chunkSize;
        }

        public Chunk Rent(bool clear = false)
        {
            Allocated++;
            Available = Math.Max(0, Available - 1);

            var buffer = new byte[ChunkSize];

            if (!clear)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = Byte.MaxValue;
                }
            }

            return new Chunk(buffer);
        }

        public void Return(ref Chunk chunk)
        {
            Assert.False(chunk.IsNull);

            chunk = default(Chunk);
            Available++;
            Allocated--;
        }
    }
}