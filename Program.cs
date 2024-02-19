using System.Net;
using System.Net.Sockets;
using System.Numerics;
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

	static (string Ip, int Port, IPEndPoint EndPoint) ParseIpEndpoint(string arg)
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
                return (ipText, portNumber, new IPEndPoint(ipAddress, portNumber));
            }
            throw new ArgumentException($"'{arg}' is NOT an valid address");
        }
        throw new ArgumentException($"'{portText}' is NOT valid a PORT number");
    }

    static bool RunServerOn(IEnumerable<string> mainArgs,
        CancellationTokenSource cancelTokenSource)
	{
		var ipServerText = mainArgs.First();
		var serverThe = ParseIpEndpoint(ipServerText);
        var listener = new TcpListener(serverThe.EndPoint);
        Log($"Start listen on {serverThe.Ip} at port {serverThe.Port}");
        listener.Start();
        CancelKeyPress += (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC
			|| e.SpecialKey == ConsoleSpecialKey.ControlBreak)
			{
                Log("Ctrl-C is found");
                cancelTokenSource.Cancel();
                Task.Delay(900).Wait();
            }
        };

        Task.Run(async () =>
        {
            try
            {
                int cntRecv = 0;
                int sizeWant = 0;
                var sizeBytes = new byte[4];
                while (true)
                {
                    var clSocket = await listener.AcceptSocketAsync(cancelTokenSource.Token);
                    var skClient = clSocket.RemoteEndPoint;
                    Log($"Accept '{skClient}'");
                    _ = Task.Run(async () =>
                    {
                        cntRecv = await clSocket.ReceiveAsync(sizeBytes, cancelTokenSource.Token);
                        sizeWant = sizeBytes.ToInt();
                        Log($"Read sizeBytes:{cntRecv}b => msgSize:{sizeWant}");
                        var buf2 = new byte[4096];
                        cntRecv = await clSocket.ReceiveAsync(buf2, cancelTokenSource.Token);
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
                await Task.Delay(20);
            }
            catch (Exception ee)
            {
                Log(ee.GetType().Name + ": " + ee.Message);
            }
        }).Wait();

        Task.Delay(20).Wait();
        return true;
	}

    static bool RunCopyTo(IEnumerable<string> mainArgs)
    {
        var ipCopyText = mainArgs.First();
        var destThe = ParseIpEndpoint(ipCopyText);
        Log($"Connect {destThe.Ip} at port {destThe.Port} ..");
        var sizeBytes = new byte[4];
        using (var clientThe = new TcpClient())
		{
            clientThe.Connect(destThe.EndPoint);
            Log("Connected");

            var demoBytes = Encoding.UTF8.GetBytes("Hi, how are you?");

            sizeBytes.FromInt(demoBytes.Length);
            clientThe.Client.SendAsync(sizeBytes, SocketFlags.None);

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

static class Helper
{
    public static void FromInt(this byte[] bytes, int value)
    {
        bytes[0] = (byte)(value & 0xFF);
        value >>= 8;
        bytes[1] = (byte)(value & 0xFF);
        value >>= 8;
        bytes[2] = (byte)(value & 0xFF);
        value >>= 8;
        bytes[3] = (byte)(value & 0xFF);
    }

    public static int ToInt(this byte[] bytes)
    {
        int rtn = bytes[3];
        rtn <<= 8; rtn += bytes[2];
        rtn <<= 8; rtn += bytes[1];
        rtn <<= 8; rtn += bytes[0];
        return rtn;
    }

    public static string ToHex(this byte[] bytes)
    {
        var rtn = new StringBuilder();
        rtn.Append($"{bytes[0]:x2}");
        rtn.Append($".{bytes[1]:x2}");
        rtn.Append($".{bytes[2]:x2}");
        rtn.Append($".{bytes[3]:x2}");
        return rtn.ToString();
    }
}
