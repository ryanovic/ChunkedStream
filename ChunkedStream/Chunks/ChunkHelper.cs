using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChunkedStream.Chunks
{
    internal static class ChunkHelper
    {
        public const int MaxChunkSize = 1 << 30;

        public static int AlignChunkSize(int chunkSize, out int shift)
        {
            if (chunkSize <= 0 || chunkSize > MaxChunkSize)
                throw new ArgumentOutOfRangeException("chunkSize", "chunkSize must be positive and less than or equal 2^30");

            shift = 2;

            int updated;
            while ((updated = 1 << shift) < chunkSize) { shift++; }
            return updated;
        }

        public static int GetAlignment(int chunkSize)
        {
            if (chunkSize <= 0 || chunkSize > MaxChunkSize)
                throw new ArgumentOutOfRangeException("chunkSize", "chunkSize must be positive and less than or equal 2^30");

            for (int shift = 0; shift < 31; shift++)
            {
                if ((1 << shift) == chunkSize)
                {
                    return shift;
                }
            }

            throw new ArgumentException("chunkSize", "chunkSize must be 2^n");
        }
    }
}
