using System.Runtime.InteropServices;

namespace InterProcessStream;

/*
 * Related topics:
 * https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute?view=net-8.0&redirectedfrom=MSDN
 * https://learn.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types
 * https://learn.microsoft.com/en-us/dotnet/framework/interop/default-marshalling-behavior#default-marshalling-for-value-types
 * https://learn.microsoft.com/en-us/dotnet/framework/interop/copying-and-pinning
 */

[Flags]
public enum StreamState
{
    None = 0,
    WriterConnected = 1,
    ReaderConnected = 1 << 1,
    StreamFinished = 1 << 2,
}

[StructLayout(LayoutKind.Sequential)]
public struct SharedState
{
    internal nuint bufferSize;
    internal nuint currentWriterIndex;
    internal nuint currentReaderIndex;
    internal StreamState streamState;
    internal byte writerSemaphore;
    internal byte readerSemaphore;

    public nuint GetAvailableSpace()
    {
        nuint availableSpace;
        if (currentReaderIndex <= currentWriterIndex)
        {
            nuint unreadData = currentWriterIndex - currentReaderIndex;
            availableSpace = bufferSize - unreadData;
        }
        else
        {
            availableSpace = currentReaderIndex - currentWriterIndex;
        }
        // We can't write the current reader index, otherwise we won't know there are
        // data to read.
        return availableSpace - 1;
    }

    public nuint GetAvailableData()
    {
        if (currentReaderIndex <= currentWriterIndex)
        {
            return currentWriterIndex - currentReaderIndex;
        }
        else
        {
            return currentWriterIndex + bufferSize - currentReaderIndex;
        }
    }

    private bool IsState(StreamState state) => (this.streamState & state) == state;

    public bool IsWriterConnected
    {
        get { return this.IsState(StreamState.WriterConnected); }
        set { this.streamState |= StreamState.WriterConnected; }
    }

    public bool IsReaderConnected
    {
        get { return this.IsState(StreamState.ReaderConnected); }
        set { this.streamState |= StreamState.ReaderConnected; }
    }

    public bool IsStreamFinished
    {
        get { return this.IsState(StreamState.StreamFinished); }
        set { this.streamState |= StreamState.StreamFinished; }
    }
}
