namespace TestConsoleApp;

class Program
{
    public static void Main(string[] args)
    {
        Args a = Args.Parse(args);
        RandomDataStreaming dataStreaming = new(
            a.HostName,
            a.IsSender,
            a.DataSize,
            a.BufferSize
        );
        dataStreaming.RunTest();
    }
}

public class Args
{
    public string HostName { get; private set; } = "test_";
    public bool IsSender { get; set; } = true;
    public long DataSize { get; set; } = 2000000000;
    public long BufferSize { get; set; } = 1000000;

    public static Args Parse(string[] args)
    {
        Args parser = new Args();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].StartsWith("--"))
                continue;

            switch (args[i].ToLower())
            {
                case "--host":
                    parser.HostName = args[i + 1];
                    break;
                case "--role":
                    parser.IsSender = args[i + 1] == "sender";
                    break;
                case "--data-size":
                    parser.DataSize = long.Parse(args[i + 1]);
                    break;
                case "--buffer-size":
                    parser.BufferSize = long.Parse(args[i + 1]);
                    break;
                default:
                    continue;
            }
        }
        return parser;
    }
}

