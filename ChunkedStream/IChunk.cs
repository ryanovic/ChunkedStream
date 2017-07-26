using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChunkedStream
{
    public interface IChunk : IDisposable
    {
        byte[] Buffer { get; }
        int Offset { get; }
        int Length { get; }
    }
}
