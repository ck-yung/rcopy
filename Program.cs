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
				var taskResult = RunCopyTo(args.Skip(1), CancelTokenSource);
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
    @"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})$")]
    private static partial Regex RegexPatternIpPort();

    [GeneratedRegex(@"^(?<host>[\D][^\s].*):(?<port>\d{1,5})$")]
    private static partial Regex RegexPatternHostPort();

    static IPEndPoint ParseIpEndpoint(string arg)
	{
        var regServer = RegexPatternIpPort().Match(arg);
        if (true == regServer.Success)
        {
            string ipText = regServer.Groups["ip"].Value;
            string portText = regServer.Groups["port"].Value;
            if (int.TryParse(portText, out var portNumber))
            {
                if (IPAddress.TryParse(ipText, out var ipAddress))
                {
                    return new IPEndPoint(ipAddress, portNumber);
                }
                throw new ArgumentException($"'{arg}' is NOT an valid address");
            }
            throw new ArgumentException($"'{portText}' is NOT valid a PORT number");
        }

        regServer = RegexPatternHostPort().Match(arg);
        if (true == regServer.Success)
        {
            string hostName = regServer.Groups["host"].Value;
            string portText = regServer.Groups["port"].Value;
            var hostAddress = Dns.GetHostAddresses(hostName)
                .First() ?? IPAddress.None;
            if (int.TryParse(portText, out var portNumber))
            {
                return new IPEndPoint(hostAddress, portNumber);
            }
            throw new ArgumentException($"'{portText}' is NOT valid a PORT number");
        }

        throw new ArgumentException($"'{arg}' is NOT in valid IP:PORT format");
    }

    static bool RunServerOn(IEnumerable<string> mainArgs,
        CancellationTokenSource cancelTokenSource)
	{
		var ipServerText = mainArgs.First();
		var endPointThe = ParseIpEndpoint(ipServerText);
        var listener = new TcpListener(endPointThe);
        Log($"Start listen on {endPointThe.Address} at port {endPointThe.Port}");
        listener.Start();
        CancelKeyPress += (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC
			|| e.SpecialKey == ConsoleSpecialKey.ControlBreak)
			{
                cancelTokenSource.Cancel();
                Task.Delay(100).Wait();
            }
        };

        WriteLine("Press Ctrl-C to break.");

        Task.Run(async () =>
        {
            try
            {
                var socketQueue = new ClientQueue();
                while (true)
                {
                    var clSocket = await listener.AcceptSocketAsync(cancelTokenSource.Token);
                    socketQueue.Add(clSocket);
                    _ = Task.Run(async () =>
                    {
                        var socketThe = socketQueue.Get();
                        if (socketThe == null)
                        {
                            Log("Error! No client connection is found!");
                            return;
                        }
                        var remoteEndPoint = socketThe.RemoteEndPoint;
                        Log($"Connected from '{remoteEndPoint}'");

                        bool statusTxfr = false;
                        int cntTxfr = 0;
                        UInt16 sizeWant = 0;
                        var sizeThe = new Byte2();
                        var buf2 = new byte[4096];
                        try
                        {
                            while (true)
                            {
                                (statusTxfr, sizeWant) = await sizeThe.Receive(socketThe,
                                    cancelTokenSource.Token);
                                if ((false == statusTxfr) || (sizeWant == 0))
                                {
                                    break;
                                }

                                System.Diagnostics.Debug.WriteLine(
                                    $"Read msgSize:{sizeWant}");
                                var buf3 = new ArraySegment<byte>(buf2, 0, sizeWant);
                                cntTxfr = await socketThe.ReceiveAsync(buf3, cancelTokenSource.Token);
                                System.Diagnostics.Debug.WriteLine(
                                    $"{DateTime.Now.ToString("s")} Recv {cntTxfr} bytes");
                                var recvText = Encoding.UTF8.GetString(buf2, 0, cntTxfr);
                                Log($"{DateTime.Now.ToString("s")} Recv '{recvText}' from '{remoteEndPoint}'");

                                sizeThe.From(0);
                                if (false == await sizeThe.Send(socketThe, cancelTokenSource.Token))
                                {
                                    Log($"Fail to send response");
                                    break;
                                }
                            }
                            await Task.Delay(20);
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
                            Log($"'{endPointThe}' dropped");
                        }
                        catch (Exception ee)
                        {
                            Log($"'{endPointThe}' {ee}");
                        }
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

    static async Task<bool> RunCopyTo(IEnumerable<string> mainArgs, CancellationTokenSource cancelTokenSource)
    {
        int cntTxfr = 0;
        bool statusTxfr = false;
        UInt16 response = 0;
        var sizeThe = new Byte2();
        var buf2 = new byte[4096];
        async Task<int> SendMesssage(Socket socket, string message)
        {
            var buf2 = Encoding.UTF8.GetBytes(message);

            sizeThe.From(buf2.Length);
            if (false == await sizeThe.Send(socket, cancelTokenSource.Token))
            {
                Log($"Fail to send msg-size");
                return -1;
            }

            cntTxfr = await socket.SendAsync(buf2, SocketFlags.None);
            if (cntTxfr != buf2.Length)
            {
                Write($"Sent message error!,");
                Write($" want={buf2.Length} but real={cntTxfr}");
                WriteLine();
                return -1;
            }

            (statusTxfr, response) = await sizeThe.Receive(socket,
                cancelTokenSource.Token);
            if (false == statusTxfr)
            {
                Log($"Fail to read response");
                return -1;
            }
            // TODO: Check if reponse is ZERO

            return sizeThe.Value();
        }

        var ipCopyText = mainArgs.First();
        var endPointThe = ParseIpEndpoint(ipCopyText);
        var connectTimeout = new CancellationTokenSource(millisecondsDelay: 3000);
        Log($"Connect {endPointThe.Address} at port {endPointThe.Port} ..");
        using (var serverThe = new TcpClient())
		{
            try
            {
                await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
                Log("Connected");
            }
            catch (Exception ee)
            {
                Log($"Connect failed! {ee.Message}");
                return false;
            }

            var socketThe = serverThe.Client;
            try
            {

                int rsp = 0;
                foreach (var msg in mainArgs.Skip(1).Union(["Bye!"]))
                {
                    rsp = await SendMesssage(socketThe, msg);
                    if (rsp != 0)
                    {
                        WriteLine($"Recv unknown response {rsp}");
                        break;
                    }
                }
            }
            catch (SocketException se)
            {
                Log($"Socket Error! {se.Message}");
            }
            catch (Exception ee)
            {
                Log($"Error! {ee}");
            }

            serverThe.Client.Shutdown(SocketShutdown.Both);
            Log("Connection is shutdown");
            Task.Delay(50).Wait();
            serverThe.Client.Close();
            Log("Connection is closed");
            Task.Delay(20).Wait();
        }
        return true;
    }
}
