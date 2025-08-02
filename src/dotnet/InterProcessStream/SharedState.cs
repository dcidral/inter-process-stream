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
    internal ulong bufferSize;
    internal ulong currentWriterIndex;
    internal ulong currentReaderIndex;
    internal StreamState streamState;
    internal byte writerSemaphore;
    internal byte readerSemaphore;

    public ulong GetAvailableData()
        {
        return (bufferSize + currentWriterIndex - currentReaderIndex) % bufferSize;
    }

    public ulong GetAvailableSpace()
        {
        return (bufferSize - GetAvailableData()) - 1;
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
