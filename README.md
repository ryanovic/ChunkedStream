# ChunkedStream

An in-memory stream implementation for .NET that uses a shared buffer to store data.

## Overview

A pool is a single, shared, presumably large enough array allocated on the managed heap and divided into chunks(exact number and size of which are specified in the constructor). Multiple stream instances allocate chunks from the common pool when needed. If a stream is closed or truncated unused chunks are returned to the pool. In situations, when there are not enough chunks - an extra one will be allocated on the heap. Such chunks are not returned and will be released by GC when no longer needed.

## Chunk

Each chunk is represented by a read-only structure that contains a pool handle(optionally) and a byte buffer(array segment).

## Pool

Pool instance is a linked list of chunks whose nodes, to minimize allocations, are stored within the shared buffer. Therefore, when some chunk is released, the first four bytes of it are used to save an offset of the next available chunk(or -1 if none available), while the top of the pool is updated to refer the chunk was just returned.

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

Set of methods above that can be used to iterate through the chunks. ***Do not*** affect the current position of the stream.

``` C#
void MoveTo(Stream target);
Task MoveToAsync(Stream target);
Task MoveToAsync(Stream target, CancellationToken cancellationToken);
```

Similar to `CopyTo` standard methods, with one notible exception - the chunk is released immediately after it's read. Reading starts from the current position to respect a semantic of standard `Stream` operations. Note that for certain reasons 'ChunkedStream' does not override `CopyTo` base methods, so default approach(with an extra buffer) will be applied here when executed.

``` C#
IBufferWriter<byte> GetWriter()
```

Creates a `IBufferWriter<byte>` instance for the stream.

## Disposing

A stream instance ***MUST*** be explicitly disposed to prevent chunks leaking from the pool. There is no override for the Finalize(destructor) method implemeted.
