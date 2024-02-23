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
				return SendTo(ipThe, argRest);
			default:
				return PrintSyntax();
		}
	}

	static bool PrintSyntax()
	{
        // {nameof(rcopy2)} to HOST:PORT - [--raw] [--name FILE-NAME]
        WriteLine($"""
        Syntax:
          {nameof(rcopy2)} to HOST:PORT FILE [FILE ..]
          {nameof(rcopy2)} to HOST:PORT --files-from FROM-FILE [FILE ..]
        Read '--files-from' (short-cut '-T') from redir console if FROM-FILE is -

        Syntax:
          {nameof(rcopy2)} on HOST:PORT [--out-dir OUT-DIR] [--md5 FLAG]

        where
          HOST is an IP or a DNS host name
          FLAG is 'on' or 'off'
        """);
		return false;
	}

	record FilesFrom(StreamReader Input, Action<StreamReader> CloseAction)
	{
		public static FilesFrom Null = new (StreamReader.Null, (_) => { });
		public void Close()
		{
			CloseAction(Input);
		}
        public IEnumerable<string> GetPathsFrom()
        {
            while (true)
            {
                var lineThe = Input.ReadLine();
                if (lineThe == null) break;
                yield return lineThe;
            }
        }
    }

    static bool SendTo(string ipTarget, IEnumerable<string> args)
	{
		FilesFrom OpenFilesFrom(string path)
		{
			if (path == "-")
			{
				if (false == Console.IsInputRedirected)
				{
                    PrintSyntax();
					return FilesFrom.Null;
                }
                return new FilesFrom(new StreamReader(OpenStandardInput()), (_) => { });
            }

			if (false == File.Exists(path))
			{
				WriteLine($"File '{path}' (--files-from) is NOT found!");
                return new FilesFrom(StreamReader.Null, (_) => { });
            }
			return new FilesFrom(File.OpenText(path), (it) => it.Close());
		}

		var filesFrom = FilesFrom.Null;
        IEnumerable<string> paths = args;

		var check2 = args.Take(2).ToArray();
        if ((check2.Length > 0) &&
			((check2[0] == "--files-from") || check2[0] == "-T"))
		{
			if (check2.Length < 2)
			{
				WriteLine("Missing filename to --file-from!");
				return false;
			}

			filesFrom = OpenFilesFrom(check2[1]);
			if (filesFrom.Input == StreamReader.Null)
			{
				return false;
			}

			paths = filesFrom.GetPathsFrom()
				.Select((it) => it.Trim())
				.Where((it) => it.Length > 0)
				.Union(args.Skip(2));
		}

		IEnumerable<Info> infos = paths
			.Distinct()
			.Select((it) => new Info(it, new FileInfo(it)))
			.Where((it) => it.File.Exists);

        var taskResult = Client.Run(ipTarget, infos);
        taskResult.Wait();
		filesFrom.Close();
		if (1 > taskResult.Result) WriteLine("No file is sent.");
        return taskResult.Result > 0;
	}
}
