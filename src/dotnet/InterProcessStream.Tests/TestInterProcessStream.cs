using System.Data;

namespace InterProcessStream.Tests;

[TestClass]
public class TestInterProcessStream
{
    [TestMethod]
    public void TestSimpleTransfer()
    {
        const int capacity = 10;
        const string hostName = nameof(TestSimpleTransfer);

        var streamWriter = InterProcessStreamWriter.CreateAsHost(hostName, capacity);
        var streamReader = InterProcessStreamReader.CreateAsClient(hostName);

        byte[] dataSent = Enumerable.Range(1, 10).Select(i => (byte)i).ToArray();

        streamWriter.Write(dataSent);

        byte[] readBuffer = new byte[10];
        streamReader.Read(readBuffer, 0, 10);

        Assert.IsTrue(readBuffer.SequenceEqual(dataSent));
    }

    /// <summary>
    /// Test if we can send a data larger than the IPC buffer by sending 15 bytes
    /// over a 10 bytes buffer.
    /// </summary>
    [TestMethod]
    public void TestLargerThanBufferTransfer()
    {
        const int capacity = 10;
        const string hostName = nameof(TestLargerThanBufferTransfer);
        TimeSpan testTimeout = TimeSpan.FromSeconds(10);

        var streamWriter = InterProcessStreamWriter.CreateAsHost(hostName, capacity);
        var streamReader = InterProcessStreamReader.CreateAsClient(hostName);

        byte[] dataSent = Enumerable.Range(1, 15).Select(i => (byte)i).ToArray();

        Thread writerThread = new(() => streamWriter.Write(dataSent));
        writerThread.Start();

        Assert.IsFalse(writerThread.Join(TimeSpan.FromMilliseconds(100)));

        byte[] readBuffer = new byte[10];
        streamReader.ReadExactlyAsync(readBuffer, 0, 10).AsTask().Wait(testTimeout);

        Assert.IsTrue(dataSent.AsSpan(0, 10).SequenceEqual(readBuffer));

        readBuffer = new byte[5];
        streamReader.Read(readBuffer, 0, 5);

        Assert.IsTrue(dataSent.Skip(10).SequenceEqual(readBuffer));
        Assert.IsTrue(writerThread.Join(TimeSpan.FromMilliseconds(100)));
    }

    [TestMethod]
    public void TestWriteTimeout()
    {
        const int capacity = 10;
        const string hostName = nameof(TestWriteTimeout);

        var streamWriter = InterProcessStreamWriter.CreateAsHost(hostName, capacity);

        Assert.IsTrue(streamWriter.CanTimeout);
        streamWriter.WriteTimeout = 100;

        byte[] dataSent = Enumerable.Range(1, 15).Select(i => (byte)i).ToArray();
        bool hasTimedOut = false;
        Task task = Task.Run(() =>
        {
            try
            {
                streamWriter.Write(dataSent, 0, 15);
            }
            catch (TimeoutException)
            {
                hasTimedOut = true;
            }
        });
        Assert.IsTrue(task.Wait(timeout: TimeSpan.FromMilliseconds(200)));
        Assert.IsTrue(hasTimedOut);
    }

    [TestMethod]
    public void TestReadTimeout()
    {
        const int capacity = 10;
        const string hostName = nameof(TestReadTimeout);

        var streamReader = InterProcessStreamReader.CreateAsHost(hostName, capacity);
        Assert.IsTrue(streamReader.CanTimeout);
        streamReader.ReadTimeout = 100;

        byte[] buffer = new byte[20];

        bool hasTimedOut = false;
        Task task = Task.Run(() =>
        {
            try
            {
                streamReader.Read(buffer, 0, 20);
            }
            catch (TimeoutException)
            {
                hasTimedOut = true;
            }
        });
        Assert.IsTrue(task.Wait(timeout: TimeSpan.FromMilliseconds(200)));
        Assert.IsTrue(hasTimedOut);
    }
}
