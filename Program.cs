using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using static System.Console;

namespace rcopy2;

public partial class Program
{
	static void Log(string message)
	{
		var s2 = DateTime.Now.ToString("s");
		WriteLine($"{s2} {message}");
	}

	static CancellationTokenSource CancelTokenSource
		= new CancellationTokenSource();

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
		switch (args[0])
		{
			case "on":
				return RunServerOn(args.Skip(1), CancelTokenSource);
			case "to":
				return RunCopyTo(args.Skip(1));
			default:
				return PrintSyntax();
		}
	}

	static bool PrintSyntax()
	{
		WriteLine($"""
			Syntax:
			  {nameof(rcopy2)} to IP:PORT - [--raw] [--name FILE-NAME]
			  {nameof(rcopy2)} to IP:PORT FILE [FILE ..]
			  {nameof(rcopy2)} to IP:PORT --from-file FROM-FILE
			Read '--from-file' from redir console if FROM-FILE is -

			Syntax:
			  {nameof(rcopy2)} on IP:PORT [--out-dir OUT-DIR]
			""");
		return false;
	}

    [GeneratedRegex(
    @"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})$",
    RegexOptions.IgnoreCase)]
    private static partial Regex RegexPatternIpPort();

	static (string Ip, int Port, IPAddress Address) ParseIpAddress(string arg)
	{
        var regexIpPort = RegexPatternIpPort();
        var regServer = regexIpPort.Match(arg);
        if (false == regServer.Success)
        {
			throw new ArgumentException($"'{arg}' is NOT in valid IP:PORT format");
        }
        string ipText = regServer.Groups["ip"].Value;
        string portText = regServer.Groups["port"].Value;
        if (int.TryParse(portText, out var portNumber))
        {
            if (IPAddress.TryParse(ipText, out var ipAddress))
            {
                return (ipText, portNumber, ipAddress);
            }
            throw new ArgumentException($"'{arg}' is NOT an valid address");
        }
        throw new ArgumentException($"'{portText}' is NOT valid a PORT number");
    }

    static bool RunServerOn(IEnumerable<string> mainArgs,
        CancellationTokenSource cancelTokenSource)
	{
		var ipServerText = mainArgs.First();
		var serverThe = ParseIpAddress(ipServerText);
		var ipEndPoint = new IPEndPoint(serverThe.Address, serverThe.Port);
        var listener = new TcpListener(ipEndPoint);
        WriteLine($"Start listen on {serverThe.Ip} at port {serverThe.Port}");
        listener.Start();
        CancelKeyPress += (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC
			|| e.SpecialKey == ConsoleSpecialKey.ControlBreak)
			{
                Log("Ctrl-C is found");
                cancelTokenSource.Cancel();
                //Log("acceptCancel.Cancel() is called");
                Task.Delay(1900).Wait();
            }
        };

        Task.Run(async () =>
        {
            //Log($"Listener task start");
            try
            {
                while (true)
                {
                    var clSocket = await listener.AcceptSocketAsync(cancelTokenSource.Token);
                    var skClient = clSocket.RemoteEndPoint;
                    //var epClient = clSocket.LocalEndPoint;
                    //Log($"Accept <<< '{skClient}' to '{epClient}'");
                    Log($"Accept '{skClient}'");
                    _ = Task.Run(async () =>
                    {
                        var buf2 = new byte[4096];
                        var cntRecv = await clSocket.ReceiveAsync(buf2, cancelTokenSource.Token);
                        Log($"Recv {cntRecv} bytes");
                        var recvText = Encoding.UTF8.GetString(buf2, 0, cntRecv);
                        WriteLine($"Recv '{recvText}'");
                        await Task.Delay(100);
                        clSocket.Shutdown(SocketShutdown.Both);
                        Log("Shutdown client");
                        await Task.Delay(100);
                        clSocket.Close();
                        Log("Close client");
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Log("Listening is stopped");
            }
            catch (Exception ee)
            {
                Log("Accept: "+ ee.GetType().Name + ": " + ee.Message);
            }

            try
            {
                await Task.Delay(20);
                listener.Stop();
                //Log($"Stop listener");
                await Task.Delay(20);
            }
            catch (Exception ee)
            {
                Log(ee.GetType().Name + ": " + ee.Message);
            }
            //Log($"Listener task stopped");
        }).Wait();

        //Log($"Bye >>>");
        Task.Delay(20).Wait();
        //Log($"Bye <<<");
        return true;
	}

    static bool RunCopyTo(IEnumerable<string> mainArgs)
    {
        var ipCopyText = mainArgs.First();
        var destThe = ParseIpAddress(ipCopyText);
        var destEp = new IPEndPoint(destThe.Address, destThe.Port);
        WriteLine($"To connect {destThe.Ip} at port {destThe.Port} ..");
        using (var clientThe = new TcpClient())
		{
            clientThe.Connect(destEp);
            Log("Connected");

            var demoBytes = Encoding.UTF8.GetBytes("Hi, how are you?");

            var sentResult = clientThe.Client.SendAsync(demoBytes, SocketFlags.None);
            sentResult.Wait();
            Log("Message sent");
            if (false == sentResult.IsCompleted || sentResult.Result != demoBytes.Length)
            {
                Write($"Sent (result:{sentResult.IsCompleted}),");
                Write($" want={demoBytes.Length} but real={sentResult.Result}");
                WriteLine();
                return false;
            }
            Task.Delay(50).Wait();
            clientThe.Client.Shutdown(SocketShutdown.Both);
            Log("Connection is shutdown");
            Task.Delay(50).Wait();
            clientThe.Client.Close();
            Log("Connection is closed");
            Task.Delay(20).Wait();
        }
        return true;
    }
}
