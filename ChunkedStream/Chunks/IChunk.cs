using System;
using System.Collections.Generic;
using System.Linq;

namespace ChunkedStream.Chunks
{
    public interface IChunk : IDisposable
    {
        byte[] Buffer { get; }
        int Offset { get; }
        int Length { get; }
    }
}
