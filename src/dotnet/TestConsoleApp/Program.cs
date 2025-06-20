﻿using System.IO.MemoryMappedFiles;

const int block1Size = 20;
const int block2Size = 10;
// create a shared memory region for the 2 blocks of data
MemoryMappedFile sharedFile = MemoryMappedFile.CreateNew("dummy_host_name", block1Size + block2Size);

// create an accessor for each memory block
// first block starts at index 0 and has 20 bytes
MemoryMappedViewAccessor block1Accessor = sharedFile.CreateViewAccessor(0, block1Size);
// second block starts at the and of the first block and has 10 bytes
MemoryMappedViewAccessor block2Accessor = sharedFile.CreateViewAccessor(block1Size, block2Size);

unsafe
{
    byte* block1 = null;
    byte* block2 = null;
    // acquire pointers for each block
    block1Accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref block1);
    block2Accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref block2);
    block2 += block2Accessor.PointerOffset;

    Console.WriteLine($"Block1 address: {(long)block1:X}");
    Console.WriteLine($"Block2 address: {(long)block2:X}");

    // clear garbage data
    Copy(CreateZeros(block1Size), block1);
    Copy(CreateZeros(block2Size), block2);
    PrintData(block1, block2);

    // write a sequence of 1 to 20 into the first memory block
    byte[] firstDataInput = Enumerable.Range(1, block1Size).Select(i => (byte)i).ToArray();
    Copy(firstDataInput, block1);
    PrintData(block1, block2);

    // write a sequence of 21 to 30 into the second memory block
    byte[] secondDataInput = Enumerable.Range(block1Size + 1, block2Size).Select(i => (byte)i).ToArray();
    Copy(secondDataInput, block2);
    PrintData(block1, block2);
}

static unsafe string ToStr(byte* data, int length)
{
    Span<byte> buffer = new Span<byte>(data, length);
    return string.Join(", ", buffer.ToArray());
}

static unsafe void Copy(byte[] source, byte* destination)
{
    fixed (byte* sourcePointer = source)
    {
        Buffer.MemoryCopy(sourcePointer, destination, source.Length, source.Length);
    }
}

static byte[] CreateZeros(int size)
{
    byte[] zeros = new byte[size];
    Array.Fill<byte>(zeros, 0);
    return zeros;
}

static unsafe void PrintData(byte* block1Data, byte* block2Data)
{
    string firstDataStr = ToStr(block1Data, block1Size);
    string secondDataStr = ToStr(block2Data, block2Size);

    Console.WriteLine("first block: " + firstDataStr);
    Console.WriteLine("second block: " + secondDataStr);
}
