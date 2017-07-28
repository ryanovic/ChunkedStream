using System;
using System.Collections.Generic;
using System.Linq;

namespace ChunkedStream
{
    public enum ChunkedStreamState
    {
        ReadWrite,
        ReadForward,
        Closed
    }
}
