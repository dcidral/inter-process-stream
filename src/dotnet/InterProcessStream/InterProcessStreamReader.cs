using System.Diagnostics;

namespace InterProcessStream;
public class InterProcessStreamReader : Stream
{
    private readonly SharedRegion sharedRegion;
    private readonly long capacity;
    private int timeout = Timeout.Infinite;

    private InterProcessStreamReader(SharedRegion sharedRegion, long capacity)
    {
        this.sharedRegion = sharedRegion;
        this.capacity = capacity;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => this.capacity;

    public override long Position
    {
        get { return 0; }
        set => throw new InvalidOperationException();
    }

    public override bool CanTimeout => true;

    public override int ReadTimeout
    {
        get => this.timeout;
        set => this.timeout = value;
    }

    internal static InterProcessStreamReader CreateAsClient(string hostName)
    {
        SharedRegion sharedRegion = SharedRegion.CreateClient(hostName);
        return new InterProcessStreamReader(sharedRegion, (long)sharedRegion.BufferSize);
    }

    public static InterProcessStreamReader CreateAsHost(string hostName, long capacity)
    {
        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);
        return new InterProcessStreamReader(sharedRegion, capacity);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return (int)this.ActualRead(buffer, (ulong)offset, (ulong)count, CancellationToken.None);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            return (int)this.ActualRead(buffer, (ulong)offset, (ulong)count, cancellationToken);
        });
    }

    private ulong ActualRead(byte[] buffer, ulong offset, ulong count, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        ulong totalDataReceived = 0;
        while (totalDataReceived < count)
        {
            if (totalDataReceived < count && this.sharedRegion.AvailableData == 0)
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
                this.sharedRegion.WaitWriter(waitTime, cancellationToken);
            }
            ulong dataReceived = this.sharedRegion.Read(
                buffer,
                offset + totalDataReceived,
                count - totalDataReceived
            );
            totalDataReceived += dataReceived;
        }

        return totalDataReceived;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException();
    }
}
