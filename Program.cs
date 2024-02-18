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

	static async Task<bool> RunMainAsync(string[] args)
	{
		if (args.Length < 2) return PrintSyntax();
		switch (args[0])
		{
			case "on":
				await RunServerOn(args.Skip(1), CancelTokenSource.Token);
				break;
			case "to":
				await RunCopyTo(args.Skip(1));
				break;
			default:
				return PrintSyntax();
		}
		return true;
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

	static (string Ip, int Port) ParseIpPort(string arg)
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
            return (ipText, portNumber);
        }
        throw new ArgumentException($"'{portText}' is NOT valid a PORT number");
    }

    static async Task RunServerOn(IEnumerable<string> mainArgs, CancellationToken cancelToken)
	{
		var ipServerText = mainArgs.First();
		var serverThe = ParseIpPort(ipServerText);
		WriteLine($"Server will be created on {serverThe.Ip} at port {serverThe.Port}");

		IPAddress ipAddress;
		if (IPAddress.TryParse(serverThe.Ip, out var ipAddressTmp))
		{
			ipAddress = ipAddressTmp;
		}
		else
		{
            throw new ArgumentException($"'{serverThe.Ip}' is NOT valid a IP address.");
        }

		var ipEndPoint = new IPEndPoint(ipAddress, serverThe.Port);
        var listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        CancelKeyPress += (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC
			|| e.SpecialKey == ConsoleSpecialKey.ControlBreak)
			{
                Log("Ctrl-C is found");
                listener.Shutdown(SocketShutdown.Both);
                Log("Listener is shutdown");
                Task.Delay(500);
                listener.Close();
                Log("Listener is closed");
                Task.Delay(500);
            }
        };
        listener.Bind(ipEndPoint);
        listener.Listen(4);

		var acceptArg = new SocketAsyncEventArgs();
        acceptArg.Completed += (sender, e) =>
		{
			Log($"Completed");
		};

		try
		{
            Log($"Accept >>>");
            var recvSocket = listener.Accept();
            Log($"Accept <<<");
			// await listener.AcceptAsync(cancelToken);
			var skClient = recvSocket.RemoteEndPoint;
			var epClient = recvSocket.LocalEndPoint;
			Log($"Accept '{skClient}' to '{epClient}'");
            var buf2 = new byte[4096];
            var cntRecv = await recvSocket.ReceiveAsync(buf2, cancelToken);
            Log($"Accept {cntRecv} bytes");
            var recvText = Encoding.UTF8.GetString(buf2, 0, cntRecv);
            WriteLine($"Recv '{recvText}'");
            await Task.Delay(500);
			recvSocket.Shutdown(SocketShutdown.Both);
            Log("Shutdown client");
            await Task.Delay(500);
			recvSocket.Close();
            Log("Close client");
            await Task.Delay(500);
            Log("Listener is shutdown");
            await Task.Delay(500);
            listener.Close();
            Log("Listener is closed");
        }
        catch (Exception ee)
		{
			Log(ee.Message);
		}

        await Task.Delay(500);
        Log($"Bye");

        return;
	}

    static async Task RunCopyTo(IEnumerable<string> mainArgs)
    {
        var ipCopyTo = mainArgs.First();
        var serverThe = ParseIpPort(ipCopyTo);
        WriteLine($"To connect {serverThe.Ip} at port {serverThe.Port} ..");

		using (var socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
		{
            socket.Connect(serverThe.Ip, serverThe.Port);
            Log("Connected");

            var demoBytes = Encoding.UTF8.GetBytes("Hi, how are you?");

            var sentResult = socket.SendAsync(demoBytes, SocketFlags.None);
            sentResult.Wait();
            Log("Message sent");
            if (false == sentResult.IsCompleted || sentResult.Result != demoBytes.Length)
            {
                WriteLine($"Sent (completed:{sentResult.IsCompleted}), want={demoBytes.Length} but real={sentResult.Result}");
                return;
            }
            await Task.Delay(500);
            socket.Shutdown(SocketShutdown.Both);
            Log("Connection is shutdown");
            await Task.Delay(500);
            socket.Close();
            Log("Connection is closed");
            await Task.Delay(500);
        }
        return;
    }
}
