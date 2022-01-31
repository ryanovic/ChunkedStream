namespace ChunkedStream
{
    /// <summary>
    /// Represents a pool of chunks of specific size.
    /// </summary>
    public interface IChunkPool
    {
        int ChunkSize { get; }

        /// <summary>
        /// Gets a chunk from the pool.
        /// </summary>
        Chunk Rent(bool clear = false);

        /// <summary>
        /// Returns a chunk to the pool. 
        /// </summary>
        void Return(ref Chunk chunk);
    }
}
