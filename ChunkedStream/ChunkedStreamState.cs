using System;
using System.Collections.Generic;

namespace ChunkedStream
{
    public enum ChunkedStreamState
    {
        ReadWrite,
        ReadForward,
        Closed
    }
}
