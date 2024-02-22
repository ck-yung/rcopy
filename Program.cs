using System.Net.Sockets;
using static System.Console;

namespace rcopy2;

public class Program
{
    public static void Main(string[] args)
    {
		try
		{
            _ = RunMainAsync(args);
		}
		catch (ArgumentException ae)
		{
			WriteLine($"Argument: {ae.Message}");
		}
		catch (SocketException se)
		{
            WriteLine($"Socket: {se.Message}");
        }
		catch (Exception ee)
		{
			WriteLine(ee.ToString());
		}
    }

	static bool RunMainAsync(string[] args)
	{
        var cancellationTokenSource = new CancellationTokenSource();

        if (args.Length < 2) return PrintSyntax();
		switch (args[0])
		{
			case "on":
				return Server.Run(args.Skip(1), cancellationTokenSource);
			case "to":
				var taskResult = Client.Run(args.Skip(1), cancellationTokenSource);
                taskResult.Wait();
                return taskResult.Result;
			default:
				return PrintSyntax();
		}
	}

	static bool PrintSyntax()
	{
		WriteLine($"""
        Syntax:
          {nameof(rcopy2)} to HOST:PORT - [--raw] [--name FILE-NAME]
          {nameof(rcopy2)} to HOST:PORT FILE [FILE ..]
          {nameof(rcopy2)} to HOST:PORT --from-file FROM-FILE
        Read '--from-file' from redir console if FROM-FILE is -

        Syntax:
          {nameof(rcopy2)} on HOST:PORT [--out-dir OUT-DIR]

        where
          HOST is an IP or a DNS host name
        """);
		return false;
	}
}
