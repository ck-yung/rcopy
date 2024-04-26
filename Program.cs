using System.Net.Sockets;
using System.Reflection.Metadata.Ecma335;

namespace rcopy;

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
            Log.Error($"Argument: {ae.Message}");
        }
        catch (SocketException se)
        {
            Log.Error($"Socket: {se.Message}");
        }
        catch (Exception ee)
        {
            Log.Error(ee.ToString());
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

        return commandThe switch
        {
            "on" => Server.Run(ipThe, argsRest),
            "to" => SendTo(ipThe, argsRest),
            _ => Helper.PrintSyntax(),
        };
    }

    static bool SendTo(string ipTarget, IEnumerable<FlagedArg> argsRest)
    {
        string[] paths = argsRest
            .Select((it) => it.Arg)
            .Distinct()
            .Where((it) => File.Exists(it) || it == "-")
            .Take(2)
            .ToArray();

        if (paths.Length > 1)
        {
            Log.Error($"Too many ('{paths[0]}','{paths[1]}') FILE!");
            return false;
        }

        var taskResult = Client.Run(ipTarget, paths[0]);
        taskResult.Wait();

        if (1 > taskResult.Result) Log.Ok("No file is sent.");
        return taskResult.Result > 0;
    }
}
