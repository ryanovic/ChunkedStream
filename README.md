# ChunkedStream
.Net stream implementation using memory chunks for data store.

Optionally memory pool can be created to store chunks within shared byte array in the LOH. In case when memory pool is used, but has no free chunks available - new chunks will be created in the managed heap instead automatically, so data can be processed without a delay.

## Requirements
.Net Framework 4.6.2

## Usage
First you need to initialize a `MemoryPool` shared instance explicitly:
```c#
ChunkedStream.InitializePool();
```
After pool is initialized - it will be shared between all `ChunkedStream` instances automatically:
```c#
var stream = new ChunkedStream()
```
In case when `ChunkedStream` is created without / before `MemoryPool` is initialized - managed heap will be used instead.

You also have two more options to customize `ChunkedStream` behaviour - first, is to force to ignore chunk pool even it's initialized by calling the following factory method:
```c#
var stream = ChunkedStream.FromMemory()
```
Second, you can explicitly specify that stream have to use `MemoryPool`, so exception will be thrown in case if no pool is available:
```c#
var stream = ChunkedStream.FromPool()
```
Also, additional feature allows to use pool a bit more efficiently for specific usage pattern - when you read a whole stream sequentially after data population stage is completed. So, once your data is ready, just call `.AsOutputStream()` method, which will indicate that only read operations will be allowed on the stream in future. Because of that every chunk can be released and put back in the pool once it's processed, without waiting a whole stream is disposed.
```c#
var stream = new ChunkedStream()
//...
stream.Write(_);
stream.Write(_);
//...

// only sequential read operations will be available after this point
stream.AsOutputStream();

//...
stream.Read(_);
stream.Read(_);
//...
```
