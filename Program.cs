using System.Net.Sockets;
using static System.Console;

namespace rcopy2;

public class Program
{
    public static void Main(string[] args)
    {
		try
		{
			Log.Init();
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
		if (args.Any((it) => "--help" == it))
		{
            return Helper.PrintSyntax(isDetailed:true);
        }

        if (args.Length < 2) return Helper.PrintSyntax();

		var commandThe = args[0];
		var ipThe = args[1];
        var argsRest = args.Skip(2).Select((it) => new FlagedArg(false, it));

        #region --verbose
        (var verbose, argsRest) = Options.Get("--verbose", argsRest);
        Log.VerboseSwitch(verbose?.Equals("on") ?? false);
        #endregion

        switch (commandThe)
		{
			case "on":
				return Server.Run(ipThe, argsRest);
			case "to":
				return SendTo(ipThe, argsRest);
			default:
				return Helper.PrintSyntax();
		}
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

    static bool SendTo(string ipTarget, IEnumerable<FlagedArg> argsRest)
	{
		FilesFrom OpenFilesFrom(string path)
		{
			if (string.IsNullOrEmpty(path)) return FilesFrom.Null;
			Log.Ok($"Files-from is '{path}'");
			if (path == "-")
			{
				if (false == Console.IsInputRedirected)
				{
                    Helper.PrintSyntax();
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

        (var pathFilesFrom, argsRest) = Options.Get("--files-from", argsRest, shortcut:"-T");
		var filesFrom = OpenFilesFrom(pathFilesFrom);

		IEnumerable<string> paths = argsRest.Select((it) => it.Arg);

		IEnumerable<Info> infos = filesFrom.GetPathsFrom()
			.Select((it) => it.Trim())
            .Where((it) => it.Length > 0)
            .Union(argsRest.Select((it) => it.Arg))
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
