using InterProcessStream;
namespace TestConsoleApp;

public class RandomDataStreaming
{
    private readonly string hostName;
    private readonly bool isSender;
    private readonly long dataSize;
    private readonly long bufferSize;

    public RandomDataStreaming(string hostName, bool isSender, long dataSize, long bufferSize)
    {
        this.hostName = hostName;
        this.isSender = isSender;
        this.dataSize = dataSize;
        this.bufferSize = bufferSize;
    }

    public void RunTest()
    {
        string role = this.isSender ? "sender" : "receiver";
        Console.WriteLine($"Starting transfer as {role}." +
            $" Data Size: {this.dataSize}," +
            $" Buffer Size: {this.bufferSize}"
        );
        byte[] data = new byte[this.dataSize];
        DateTime start, end;
        if (this.isSender)
        {
            Random random = new Random();
            random.NextBytes(data);

            InterProcessStreamWriter writer = InterProcessStreamWriter.CreateAsHost(
                this.hostName,
                this.bufferSize
            );
            writer.WaitReader();
            start = DateTime.Now;
            writer.Write(data);
            end = DateTime.Now;
        }
        else
        {
            InterProcessStreamReader reader = InterProcessStreamReader.CreateAsClient(
                this.hostName
            );
            reader.WaitWriter();
            start = DateTime.Now;
            reader.Read(data);
            end = DateTime.Now;
        }
        // calculate the md5 of data:
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash = md5.ComputeHash(data);
            Console.WriteLine("MD5: " + BitConverter.ToString(hash).Replace("-", ""));
        }
        TimeSpan duration = end - start;
        double transferRate = (this.dataSize / 1000000) / duration.TotalSeconds;
        Console.WriteLine($"{transferRate}mB/s");
        Console.WriteLine($"Duration: {duration}");
    }
}
