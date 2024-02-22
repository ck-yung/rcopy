using System.Net.Sockets;
using System.Threading;
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
        if (args.Length < 2) return PrintSyntax();
		var commandThe = args[0];
		var ipThe = args[1];
		var argRest = args.Skip(2);
		switch (commandThe)
		{
			case "on":
				return Server.Run(ipThe, argRest);
			case "to":
				return SendTo(ipThe, argRest.ToArray());
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
          {nameof(rcopy2)} to HOST:PORT --files-from FROM-FILE
        Read '--files-from' (short-cut '-T') from redir console if FROM-FILE is -

        Syntax:
          {nameof(rcopy2)} on HOST:PORT [--out-dir OUT-DIR]

        where
          HOST is an IP or a DNS host name
        """);
		return false;
	}

	static bool SendTo(string ipTarget, string[] args)
	{
		(StreamReader input, Action<StreamReader> close)
			OpenFilesFrom(string fromFile)
		{
			if (fromFile == "-")
			{
				if (false == Console.IsInputRedirected)
				{
                    PrintSyntax();
                    return (StreamReader.Null, (_) => { });
                }
                return (new StreamReader(OpenStandardInput()), (_) => { });
            }

			if (false == File.Exists(fromFile))
			{
				WriteLine($"File '{fromFile}' (--files-from) is NOT found!");
                return (StreamReader.Null, (_) => { });
            }
			return (File.OpenText(fromFile), (it) => it.Close());
		}

		Info[] infos = Array.Empty<Info>();

        if ((args.Length > 0) &&
			((args[0] == "--files-from") || args[0] == "-T"))
		{
			if (args.Length < 2)
			{
				WriteLine("Missing filename to --file-from!");
				return false;
			}

			(var input, var closeThe) = OpenFilesFrom(args[1]);
			if (input == StreamReader.Null)
			{
				return false;
			}

			infos = input.ReadToEnd()
				.Split('\n', '\r')
				.Select((it) => it.Trim())
				.Where((it) => it.Length > 0)
				.Distinct()
                .Select((it) => new Info(it, new FileInfo(it)))
                .Where((it) => it.File.Exists)
                .ToArray();
            closeThe(input);
		}
		else
		{
            infos = args
				.Distinct()
                .Select((it) => new Info(it, new FileInfo(it)))
                .Where((it) => it.File.Exists)
                .ToArray();
        }

        if (infos.Length == 0) return false;
        var taskResult = Client.Run(ipTarget, infos);
        taskResult.Wait();
        return taskResult.Result;
	}
}
