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

        ulong totalDataCopied = 0;
        while (totalDataCopied < count && this.sharedState->GetAvailableSpace() > 0)
        {
            // Using a copy avoids issues with race conditions
            SharedState stateSnapshot = *this.sharedState;
            ulong dataOffset = totalDataCopied + offset;
            ulong sharedBufferOffset = (stateSnapshot.currentWriterIndex + 1) % stateSnapshot.bufferSize;
            ulong remainingData = Math.Min(count - totalDataCopied, stateSnapshot.GetAvailableSpace());
            ulong copySize;

            if (sharedBufferOffset < stateSnapshot.currentReaderIndex)
            {
                copySize = remainingData;
            }
            else
            {
                ulong distanceToEnd = stateSnapshot.bufferSize - sharedBufferOffset;
                copySize = Math.Min(remainingData, distanceToEnd);
            }

            fixed (byte* dataPtr = data)
            {
                Buffer.MemoryCopy(
                    dataPtr + dataOffset,
                    this.sharedBuffer + sharedBufferOffset,
                    copySize,
                    copySize
                );
            }
            
            this.sharedState->currentWriterIndex = (this.sharedState->currentWriterIndex + copySize) % this.sharedState->bufferSize;
            totalDataCopied += copySize;
            this.writerSemaphore.Signal();
        }
        return totalDataCopied;
    }

    public unsafe ulong Read(byte[] readBuffer)
    {
        return this.Read(readBuffer, 0, (ulong)readBuffer.LongLength);
    }

    public unsafe ulong Read(byte[] readBuffer, ulong offset, ulong count)
    {
        AssertBufferSize(readBuffer, offset, count);

        ulong totalDataCopied = 0;
        while (totalDataCopied < count && this.GetSharedState()->GetAvailableData() > 0)
        {
            // Using a copy avoids issues with race conditions
            SharedState stateSnapshot = *this.sharedState;
            ulong remainingReadSize = count - totalDataCopied;
            ulong maxDataToRead = Math.Min(remainingReadSize, stateSnapshot.GetAvailableData());
            ulong sharedBufferOffset = (stateSnapshot.currentReaderIndex + 1) % this.sharedState->bufferSize;
            ulong copyCount;
            if (sharedBufferOffset < stateSnapshot.currentWriterIndex)
            {
                copyCount = maxDataToRead;
            }
            else
            {
                ulong distanceToEnd = this.sharedState->bufferSize - sharedBufferOffset;
                copyCount = Math.Min(maxDataToRead, distanceToEnd);
            }

            fixed (byte* readPointer = readBuffer)
            {
                byte* destination = readPointer + offset + totalDataCopied;
                ulong destinationSizeInBytes = (ulong)readBuffer.LongLength - (offset + totalDataCopied);
                Buffer.MemoryCopy(
                    this.sharedBuffer + sharedBufferOffset,
                    destination,
                    destinationSizeInBytes,
                    copyCount
                );
            }

            this.sharedState->currentReaderIndex = (this.sharedState->currentReaderIndex + copyCount) % this.sharedState->bufferSize;
            totalDataCopied += copyCount;

            this.readerSemaphore.Signal();
        }
        return totalDataCopied;
    }

    public bool WaitReader(TimeSpan timeout, CancellationToken cancelToken = default)
    {
        return this.readerSemaphore.Wait(timeout, cancelToken);
    }

    public bool WaitWriter(TimeSpan timeout, CancellationToken cancelToken = default)
    {
        return this.writerSemaphore.Wait(timeout, cancelToken);
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
