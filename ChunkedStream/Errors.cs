namespace Ry.IO
{
    using System;

    internal static class Errors
    {
        public static string PoolMinChunkSize(int minSize) => $"Chunk size can't be less than {minSize} bytes.";

        public static string PoolMinChunkCount(int minCount) => $"A pool must have at least {minCount} chunk(s).";

        public static string PoolMaxBufferSize(int maxSize) => $"Total pool size can't exceed 0x{maxSize:x} bytes.";

        public static string ChunkIsNull() => $"The chunk has not been initialized.";

        public static string ChunkIsImposter() => $"The chunk does not belong to this pool.";

        public static string NegativeNumber(string name) => $"{name} must be non-negative.";

        public static string StreamMaxSize() => $"Stream is too large.";

        public static string StreamIsChanged() => $"Stream can't be altered during the operation.";

        public static string ReversedOrder() => $"Specified range is reversed.";
    }
}