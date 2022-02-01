An in-memory stream implementation for .NET that uses a shared buffer to store data.

## Overview

A `ChunkedStream` instance reads and writes data using bytes buffers from a shared pool. The pool is a single, presumably large enough array allocated on the managed heap and divided into chunks(exact number and size of which are specified in the constructor). When the stream is closed or truncated, unused chunks are returned to the pool and become available to other stream instances. In situations, when there are no chunks left in the pool - an extra one will be allocated on the heap to satisfy a pending request. Such chunks are not returned and will be released by GC when no longer needed.

## Chunk

Each chunk is represented by a read-only structure that contains a pool handle(optionally) and a byte buffer(array segment) reference.

## Pool

A `ChunkPool` type is implemented as a linked list whose nodes, to optimize memory and minimize allocations, are stored within the shared buffer. The pool keeps a single offset to the next chunk available. When some chunk is released, a buffer segment dedicated for the chunk is used to store the current top, which, in turn, will be updated with an offset of the chunk that was just returned. And vice versa, when a chunk is requested, the pool returns the one from the top, while the reference to the next available chunk will be updated with an offset was previously saved in the chunk. 

## Stream

The ChunkedStreams class implements stadard operations with the following additions:

``` C#
byte[] ToArray();
```

Cpies the entire stream to an array.

``` C#
void ForEach(Action<ArraySegment<byte>> action);
void ForEach(long from, long to, Action<ArraySegment<byte>> action);
Task ForEachAsync(Func<ArraySegment<byte>, Task> asyncAction);
Task ForEachAsync(long from, long to, Func<ArraySegment<byte>, Task> asyncAction);
```

Set of methods that can be used to iterate through the chunks. ***Do not*** affect the current position of the stream.

``` C#
void MoveTo(Stream target);
Task MoveToAsync(Stream target);
Task MoveToAsync(Stream target, CancellationToken cancellationToken);
```

Similar to `CopyTo` standard methods, with one notible exception - the chunk is released immediately after it's read. Reading starts from the current position to respect a semantic of standard `Stream` operations. Note that for certain reasons `ChunkedStream` does not override `CopyTo` base methods, so default approach(with an extra buffer) will be applied here when executed.

``` C#
IBufferWriter<byte> GetWriter()
```

Creates a `IBufferWriter<byte>` instance for the stream.

## Disposing

A stream instance ***MUST*** be explicitly disposed to prevent chunks leaking from the pool. There is no override for the Finalize(destructor) method implemeted.
