using System.Diagnostics;

namespace InterProcessStream;

public class InterProcessStreamWriter : Stream
{
    private readonly SharedRegion sharedRegion;
    private readonly long capacity;
    private int timeout = Timeout.Infinite;

    private InterProcessStreamWriter(SharedRegion sharedRegion, long capacity)
    {
        this.sharedRegion = sharedRegion;
        this.capacity = capacity;
        unsafe
        {
            sharedRegion.GetSharedState()->IsWriterConnected = true;
        }
    }

    public static InterProcessStreamWriter CreateAsHost(string hostName, long capacity)
    {
        var sharedRegion = SharedRegion.CreateHost(hostName, capacity);
        return new InterProcessStreamWriter(sharedRegion, capacity);
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => this.capacity;

    public override long Position
    {
        get
        {
            // TODO: Should we return the current writer index?
            return 0;
        }
        set => throw new InvalidOperationException();
    }

    public override bool CanTimeout => true;

    public override int WriteTimeout
    {
        get => this.timeout;
        set => this.timeout = value;
    }

    public override void Flush()
    {
        // TODO: Check if we need to flush the underlying memory mapped file.
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException($"Reading is not supported for {nameof(InterProcessStreamWriter)}.");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new InvalidOperationException();
    }

    public override void SetLength(long value)
    {
        throw new InvalidOperationException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        this.ActualWrite(buffer, (ulong)offset, (ulong)count, CancellationToken.None);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await Task.Run(() => this.ActualWrite(buffer, (ulong)offset, (ulong)count, cancellationToken));
    }

    private void ActualWrite(byte[] buffer, ulong offset, ulong count, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        ulong dataSent = 0;
        while (dataSent < count)
        {
            if (dataSent < count && this.sharedRegion.AvailableSpace == 0)
            {
                TimeSpan waitTime;
                if (this.timeout != Timeout.Infinite)
                {
                    if (stopwatch.Elapsed.TotalMilliseconds > this.timeout)
                        throw new TimeoutException();

                    waitTime = TimeSpan.FromMilliseconds(this.timeout) - stopwatch.Elapsed;
                }
                else
                {
                    // TODO: Should we set a small default wait time in this case?
                    waitTime = TimeSpan.MaxValue;
                }
                this.sharedRegion.WaitReader(waitTime, cancellationToken);
            }
            ulong dataWritten = this.sharedRegion.Write(
                buffer,
                offset + dataSent,
                count - dataSent
            );
            dataSent += dataWritten;
        }
    }

    public void WaitReader()
    {
        // TODO: currently used for test only
        unsafe
        {
            var sharedState = this.sharedRegion.GetSharedState();
            SpinWait spinner = new();
            while (!sharedState->IsReaderConnected)
            {
                spinner.SpinOnce();
            }
            return;
        }
    }
}
