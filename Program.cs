using System.Net.Sockets;

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
            Console.WriteLine($"Argument: {ae.Message}");
        }
        catch (SocketException se)
        {
            Console.WriteLine($"Socket: {se.Message}");
        }
        catch (Exception ee)
        {
            Console.WriteLine(ee.ToString());
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
        (var verbose, argsRest) = Options.Parse("--verbose", argsRest);
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

    static bool SendTo(string ipTarget, IEnumerable<FlagedArg> argsRest)
    {
        string[] paths = argsRest
            .Select((it) => it.Arg)
            .Distinct()
            .Where((it) => File.Exists(it))
            .Take(2)
            .ToArray();

        if (paths.Length > 1)
        {
            Console.WriteLine($"Too many ('{paths[0]}','{paths[1]}') FILE!");
            return false;
        }

        var taskResult = Client.Run(ipTarget, new Info(paths[0], new FileInfo(paths[0])));
        taskResult.Wait();

        if (1 > taskResult.Result) Console.WriteLine("No file is sent.");
        return taskResult.Result > 0;
    }
}
