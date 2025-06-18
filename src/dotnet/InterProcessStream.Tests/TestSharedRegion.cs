using System.Buffers;
using System.Runtime.InteropServices;

namespace InterProcessStream.Tests;

[TestClass]
public class TestSharedRegion
{
    [TestMethod]
    public void TestCreateHost()
    {
        string hostName = "TestCreateHost";
        long capacity = 100;

        var region = SharedRegion.CreateHost(hostName, capacity);
        unsafe
        {
            SharedState* state = region.GetSharedState();
            Assert.IsFalse(state->IsWriterConnected);
            Assert.IsFalse(state->IsReaderConnected);
            Assert.IsFalse(state->IsStreamFinished);
        }
    }

    [TestMethod]
    public void TestWrite()
    {
        const int capacity = 10;
        const string hostName = nameof(TestWrite);

        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);
        unsafe
        {
            byte* sharedData = sharedRegion.GetSharedMemory();
            Marshal.Copy((byte[])[0, 0, 0, 0], 0, new nint(sharedData), 4);

            Span<byte> sharedDataView = new Span<byte>(sharedData, 4);

            Assert.IsTrue(sharedDataView.SequenceEqual((byte[])[0, 0, 0, 0]));

            sharedRegion.Write([1, 2, 3, 4]);

            Assert.IsTrue(sharedDataView.SequenceEqual((byte[])[1, 2, 3, 4]));

            // Test write with offset
            sharedRegion.Write([9, 8, 7, 6, 5, 4, 3], 2, 4);
            sharedDataView = new Span<byte>(sharedData + 4, 4);

            Assert.IsTrue(sharedDataView.SequenceEqual((byte[])[7, 6, 5, 4]));
        }
    }

    [TestMethod]
    public void TestRead()
    {
        const int capacity = 10;
        const string hostName = nameof(TestRead);

        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);
        byte[] dataSent = [1, 2, 3, 4];

        sharedRegion.Write(dataSent);

        byte[] buffer = new byte[4];
        sharedRegion.Read(buffer);

        Assert.IsTrue(buffer.SequenceEqual(dataSent));

        // Test read with offset
        sharedRegion.Write(dataSent);
        buffer = new byte[8];
        sharedRegion.Read(buffer, 2u, 4u);

        byte[] expected = [0, 0, 1, 2, 3, 4, 0, 0];
        Assert.IsTrue(expected.SequenceEqual(buffer));
    }


    [TestMethod]
    public void TestWriteEndOfBuffer()
    {
        const int capacity = 10;
        const string hostName = nameof(TestWriteEndOfBuffer);
        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);

        // Move internal write and read positions to force the next write
        // to start at the middle of the buffer.
        sharedRegion.Write([0, 0, 0, 0, 0]);
        sharedRegion.Read([0, 0, 0, 0, 0], 0, 5);

        byte[] dataSent = Enumerable.Range(1, 10).Select(i => (byte)i).ToArray();
        sharedRegion.Write(dataSent);

        byte[] readBuffer = new byte[dataSent.Length];
        sharedRegion.Read(readBuffer, 0, (ulong)dataSent.Length);

        Assert.IsTrue(readBuffer.SequenceEqual(dataSent));
        unsafe
        {
            SharedState* state = sharedRegion.GetSharedState();
            byte* buffer = sharedRegion.GetSharedMemory();
            // Internally, SharedRegion creates buffer with an additional position for the
            // current write and read index to rest
            var bufferData = new Span<byte>(buffer, capacity + 1);
            byte[] expectedBufferData = [7, 8, 9, 10, 0, 1, 2, 3, 4, 5, 6];
            Assert.IsTrue(bufferData.SequenceEqual(expectedBufferData));
        }
    }

    [TestMethod]
    public void TestWriteWithoutAvailableSpace()
    {
        const int capacity = 10;
        const string hostName = nameof(TestWriteWithoutAvailableSpace);
        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);

        // Occupy the first 4 spaces from the write buffer so the next write will
        // not be able to write all data
        Assert.AreEqual(4u, sharedRegion.Write([0, 0, 0, 0]));

        byte[] data = Enumerable.Range(1, 10).Select(i => (byte)i).ToArray();
        ulong dataSent = sharedRegion.Write(data);

        // Only the first 6 bytes should be written
        Assert.AreEqual(6u, dataSent);

        // Without available space, no data should be written
        Assert.AreEqual(0u, sharedRegion.Write([0, 0, 0, 0]));
    }

    [TestMethod]
    public void TestReadWithoutAvailableData()
    {
        const int capacity = 10;
        const string hostName = nameof(TestReadWithoutAvailableData);
        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);

        // Test no data available.
        byte[] readBuffer = new byte[10];
        Assert.AreEqual(0u, sharedRegion.Read(readBuffer));

        // Test less available data than requested
        sharedRegion.Write([1, 2, 3, 4]);
        Array.Fill<byte>(readBuffer, 0);

        Assert.AreEqual(4u, sharedRegion.Read(readBuffer));

        byte[] expected = [1, 2, 3, 4, 0, 0, 0, 0, 0, 0];
        Assert.IsTrue(readBuffer.SequenceEqual(expected));
    }

    [TestMethod]
    public void TestInvalidBufferSize()
    {
        const int capacity = 10;
        const string hostName = nameof(TestInvalidBufferSize);
        SharedRegion sharedRegion = SharedRegion.CreateHost(hostName, capacity);

        byte[] writeBuffer = new byte[5];
        // Writing more than the buffer size
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => sharedRegion.Write(writeBuffer, 0, 10)
        );
        // Writing with offset
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => sharedRegion.Write(writeBuffer, 2, 5)
        );

        byte[] readBuffer = new byte[5];
        // Reading more than the buffer size
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => sharedRegion.Read(readBuffer, 0, 10)
        );
        // Reading with offset
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => sharedRegion.Read(readBuffer, 2, 5)
        );
    }
}
