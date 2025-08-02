using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InterProcessStream;

public unsafe class SharedRegion : IDisposable
{
    private readonly MemoryMappedFile sharedFile;
    private readonly MemoryMappedViewAccessor sharedStateView;
    private SharedState* sharedState = null;
    private byte* sharedBuffer = null;
    private readonly IPCSemaphore readerSemaphore;
    private readonly IPCSemaphore writerSemaphore;

    // TODO: Ensure the current platform uses a shared memory file directory.
    // Maybe checking if the directory exists is enought?
    const string LINUX_SMF_DIR = "/dev/shm/";

    public SharedRegion(MemoryMappedFile sharedFile, MemoryMappedViewAccessor sharedStateView)
    {
        this.sharedFile = sharedFile;
        this.sharedStateView = sharedStateView;

        /*
         * Shared Memory Layout:
         * -------------
         * A single contiguous byte array is used to store both the 'SharedState' struct and a byte buffer.
         *
         * 1. SharedState Struct:
         *    - Occupies the first sizeof(SharedState) bytes of the array.
         *
         * 2. Byte Buffer:
         *    - Occupies the remaining portion of the array, immediately following SharedState.
         *    - Used for the circular buffer to store the data to be shared between processes.
         *
         *   +---------------------------+---------+
         *   | SharedState  | byte buffer ...      |
         *   +---------------------------+---------+
         *
         * When accessing the shared byte buffer, the start address is:
         *     byte* buffer = basePointer + sizeof(SharedState);
         *
         * This organization allows both structured access via SharedState and
         * unstructured access through the byte buffer over the same memory block.
         */
        byte* sharedMemoryPointer = null;
        this.sharedStateView.SafeMemoryMappedViewHandle.AcquirePointer(ref sharedMemoryPointer);
        this.sharedState = (SharedState*)sharedMemoryPointer;

        this.sharedBuffer = sharedMemoryPointer + Marshal.SizeOf<SharedState>();

        this.readerSemaphore = new IPCSemaphore(&this.sharedState->readerSemaphore, false);
        this.writerSemaphore = new IPCSemaphore(&this.sharedState->writerSemaphore, false);
    }

    ~SharedRegion()
    {
        this.ActualDispose();
    }

    public ulong BufferSize => this.sharedState->bufferSize;

    /// <summary>
    /// The space available to write, in bytes.
    /// </summary>
    public ulong AvailableSpace => this.sharedState->GetAvailableSpace();

    /// <summary>
    /// The number of bytes available to read.
    /// </summary>
    public ulong AvailableData => this.sharedState->GetAvailableData();

    private void ActualDispose()
    {
        try
        {
            this.sharedStateView.SafeMemoryMappedViewHandle.ReleasePointer();
            this.sharedStateView.Dispose();
        }
        catch
        {
            // Might throw an error if the handle was already disposed.
        }

        try
        {
            this.sharedFile.Dispose();
        }
        catch
        {
            // Might throw an error if the OS removed the memory file for some reason.
        }
    }

    public void Dispose()
    {
        this.ActualDispose();
        GC.SuppressFinalize(this);
    }

    public static SharedRegion CreateHost(string hostName, long capacity)
    {
        int sharedStateSize = Marshal.SizeOf<SharedState>();
        long bufferSize = capacity + 1;
        long totalSharedMemorySize = bufferSize + sharedStateSize;

        MemoryMappedFile sharedFile;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            sharedFile = MemoryMappedFile.CreateNew(hostName, totalSharedMemorySize);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string filePath = Path.Combine(LINUX_SMF_DIR, hostName);
            sharedFile = MemoryMappedFile.CreateFromFile(
                filePath,
                FileMode.CreateNew,
                null,
                totalSharedMemorySize
            );
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"{nameof(SharedRegion)} is only supported on Windows and Linux."
            );
        }

        MemoryMappedViewAccessor sharedMemoryAccesor = sharedFile.CreateViewAccessor(0, totalSharedMemorySize);
        unsafe
        {
            // Initialize the SharedState structure inside the shared memory region
            byte* sharedStatePointer = null;
            sharedMemoryAccesor.SafeMemoryMappedViewHandle.AcquirePointer(ref sharedStatePointer);
            SharedState* sharedState = (SharedState*)sharedStatePointer;
            sharedState->currentReaderIndex = 0;
            sharedState->currentWriterIndex = 0;
            sharedState->bufferSize = (ulong)bufferSize;
        }

        return new SharedRegion(sharedFile, sharedMemoryAccesor);
    }

    public static SharedRegion CreateClient(string hostName)
    {
        MemoryMappedFile sharedFile;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            sharedFile = MemoryMappedFile.OpenExisting(hostName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string filePath = Path.Combine(LINUX_SMF_DIR, hostName);
            sharedFile = MemoryMappedFile.CreateFromFile(
                filePath,
                FileMode.Open
            );
        }
        else
            throw new PlatformNotSupportedException($"{nameof(SharedRegion)} is only supporte on Windows and Linux");

        int sharedStateSize = Marshal.SizeOf<SharedState>();
        using var tempViewAccessor = sharedFile.CreateViewAccessor(0, sharedStateSize);
        unsafe
        {
            byte* stateData = null;
            tempViewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref stateData);
            SharedState* sharedState = (SharedState*)stateData;
            MemoryMappedViewAccessor sharedMemoryAccesor = sharedFile.CreateViewAccessor(
                0,
                sharedStateSize + (long)sharedState->bufferSize
            );
            return new SharedRegion(sharedFile, sharedMemoryAccesor);
        }
    }

    public unsafe ulong Write(byte[] data)
    {
        return this.Write(data, 0, (ulong)data.LongLength);
    }

    public unsafe ulong Write(byte[] data, ulong offset, ulong count)
    {
        AssertBufferSize(data, offset, count);

        ulong dataCopied = 0;
        ReadWriteInstructions instruction = new();

        while (dataCopied < count && this.sharedState->GetAvailableSpace() > 0)
        {
            ulong distanceToEnd = this.sharedState->bufferSize - this.sharedState->currentWriterIndex;
            ulong writeBlockSize = Math.Min(count - dataCopied, this.sharedState->GetAvailableSpace());

            instruction.subjectOffset = offset + dataCopied;
            if (distanceToEnd == 0)
            {
                instruction.startIndex = 0;
                instruction.count = writeBlockSize;
            }
            else
            {
                instruction.startIndex = this.sharedState->currentWriterIndex;
                instruction.count = Math.Min(writeBlockSize, distanceToEnd);
            }

            fixed (byte* source = data)
            {
                Buffer.MemoryCopy(
                    source + instruction.subjectOffset,
                    this.sharedBuffer + instruction.startIndex,
                    instruction.count,
                    instruction.count
                );
            }
            this.sharedState->currentWriterIndex = (nuint)(instruction.startIndex + instruction.count);
            dataCopied += instruction.count;

            // TODO: should I move the timeout logic to the SharedRegion class?
            this.writerSemaphore.Signal();
        }
        return dataCopied;
    }

    public unsafe ulong Read(byte[] readBuffer)
    {
        return this.Read(readBuffer, 0, (ulong)readBuffer.LongLength);
    }

    public unsafe ulong Read(byte[] readBuffer, ulong offset, ulong count)
    {
        AssertBufferSize(readBuffer, offset, count);

        var instruction = new ReadWriteInstructions();
        ulong dataCopied = 0;
        while (dataCopied < count && this.GetSharedState()->GetAvailableData() > 0)
        {
            ulong distanceToEnd = this.sharedState->bufferSize - this.sharedState->currentReaderIndex;
            ulong dataToRead = Math.Min(count - dataCopied, this.sharedState->GetAvailableData());
            ulong maxDataToRead = Math.Min(this.sharedState->GetAvailableData(), dataToRead);
            instruction.startIndex = distanceToEnd > 0 ? this.sharedState->currentReaderIndex : 0;
            if (this.sharedState->currentWriterIndex > instruction.startIndex)
            {
                instruction.count = maxDataToRead;
            }
            else
            {
                instruction.count = Math.Min(maxDataToRead, distanceToEnd);
            }

            fixed (byte* readPointer = readBuffer)
            {
                byte* destination = readPointer + offset + dataCopied;
                ulong destinationSizeInBytes = (ulong)readBuffer.LongLength - (offset + dataCopied);
                Buffer.MemoryCopy(
                    this.sharedBuffer + instruction.startIndex,
                    destination,
                    destinationSizeInBytes,
                    instruction.count
                );
            }
            this.sharedState->currentReaderIndex += (nuint)instruction.count;
            dataCopied += instruction.count;

            this.readerSemaphore.Signal();
        }
        return dataCopied;
    }

    public bool WaitReader(TimeSpan timeout, CancellationToken cancelToken = default)
    {
        return this.readerSemaphore.Wait(timeout, cancelToken);
    }

    public bool WaitWriter(TimeSpan timeout, CancellationToken cancelToken = default)
    {
        return this.writerSemaphore.Wait(timeout, cancelToken);
    }

    struct ReadWriteInstructions
    {
        internal ulong startIndex;
        internal ulong count;
        // Subject is incoming data buffer when writing, or the outgoing data buffer when reading
        internal ulong subjectOffset;
    }

    internal SharedState* GetSharedState()
    {
        return this.sharedState;
    }

    internal byte* GetSharedMemory()
    {
        return this.sharedBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertBufferSize(byte[] buffer, ulong offset, ulong count)
    {
        if (count + offset > (ulong)buffer.LongLength)
        {
            throw new ArgumentOutOfRangeException();
        }
    }
}
