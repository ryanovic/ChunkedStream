namespace ChunkedStream
{
    public interface IChunkPool
    {
        int ChunkSize { get; }

        Chunk Rent(bool clear = false);
        void Return(ref Chunk chunk);
    }
}
