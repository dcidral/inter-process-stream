namespace InterProcessStream.Tests;

[TestClass]
public class TestIPCSemaphore
{
    [TestMethod]
    public unsafe void TestSignal()
    {
        byte signalValue = 0;
        byte* signalAddress = &signalValue;

        IPCSemaphore semaphore = new IPCSemaphore(signalAddress, false);
        Assert.IsFalse(semaphore.IsSignaled);

        semaphore.Signal();

        Assert.IsTrue(semaphore.IsSignaled);
    }

    [TestMethod]
    public unsafe void TestWaitSignal()
    {
        byte signalValue = 0;
        byte* signalAddress = &signalValue;

        IPCSemaphore waiterSemaphore = new IPCSemaphore(signalAddress, false);
        IPCSemaphore signalerSemaphore = new IPCSemaphore(signalAddress, false);

        Assert.IsFalse(waiterSemaphore.IsSignaled || signalerSemaphore.IsSignaled);

        var taskWaiter = Task.Run(() => waiterSemaphore.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsFalse(taskWaiter.Wait(TimeSpan.FromMilliseconds(100)));

        signalerSemaphore.Signal();

        Assert.IsTrue(taskWaiter.Wait(TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(taskWaiter.IsCompleted);
        Assert.IsTrue(taskWaiter.Result);
    }

    [TestMethod]
    public unsafe void TestWaitSignalAbort()
    {
        byte signalValue = 0;
        byte* signalAddress = &signalValue;

        IPCSemaphore waiterSemaphore = new IPCSemaphore(signalAddress, false);

        CancellationTokenSource cancellationToken = new CancellationTokenSource();

        Assert.IsFalse(waiterSemaphore.IsSignaled);

        var taskWaiter = Task.Run(() => waiterSemaphore.Wait(TimeSpan.FromSeconds(10), cancellationToken.Token));
        Assert.IsFalse(taskWaiter.Wait(TimeSpan.FromMilliseconds(100)));

        cancellationToken.Cancel();

        Assert.IsTrue(cancellationToken.Token.IsCancellationRequested);

        Assert.IsTrue(taskWaiter.Wait(TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(taskWaiter.IsCompleted);
        Assert.IsFalse(taskWaiter.Result, "Canceled Wait should always return false.");
    }
}
