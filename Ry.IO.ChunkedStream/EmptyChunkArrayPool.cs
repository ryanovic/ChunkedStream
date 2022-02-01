namespace Ry.IO
{
    using System;
    using System.Buffers;

    /// <summary>
    /// Represents a pool that allocates arrays directly on the heap.
    /// </summary>
    internal sealed class EmptyChunkArrayPool : ArrayPool<Chunk>
    {
        private const int MaxLengthToAlign = 0x40000000;

        public static EmptyChunkArrayPool Instance { get; } = new EmptyChunkArrayPool();

        public override Chunk[] Rent(int minimumLength)
        {
            if (minimumLength < 0)
            {
                throw new ArgumentException(Errors.NegativeNumber("Array length"), nameof(minimumLength));
            }

            if (minimumLength > MaxLengthToAlign)
            {
                return new Chunk[minimumLength];
            }

            if (minimumLength == 0)
            {
                return Array.Empty<Chunk>();
            }

            // Round up to the next highest power of 2.
            // https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            minimumLength--;
            minimumLength |= minimumLength >> 1;
            minimumLength |= minimumLength >> 2;
            minimumLength |= minimumLength >> 4;
            minimumLength |= minimumLength >> 8;
            minimumLength |= minimumLength >> 16;
            minimumLength++;

            return new Chunk[minimumLength];
        }

        public override void Return(Chunk[] array, bool clearArray = false)
        {
            // Just do nothing. 
        }
    }
}