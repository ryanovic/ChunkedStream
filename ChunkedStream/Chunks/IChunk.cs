using System;
using System.Collections.Generic;

namespace ChunkedStream.Chunks
{
    public interface IChunk : IDisposable
    {
        byte[] Buffer { get; }
        int Offset { get; }
        int Length { get; }
    }
}
