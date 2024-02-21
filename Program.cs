using System;
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
                Log("Ctrl-C is found");
                cancelTokenSource.Cancel();
                Task.Delay(900).Wait();
            }
        };

        WriteLine("Press Ctrl-C to break.");

        Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var clSocket = await listener.AcceptSocketAsync(cancelTokenSource.Token);
                    var skClient = clSocket.RemoteEndPoint;
                    Log($"From '{skClient}'");
                    _ = Task.Run(async () =>
                    {
                        int cntRecv = 0;
                        UInt16 sizeWant = 0;
                        var sizeByte2 = new byte[2];
                        var buf2 = new byte[4096];
                        (var socketThe, var clientThe) = (clSocket,  skClient);
                        try
                        {
                            while (true)
                            {
                                cntRecv = await socketThe.ReceiveAsync(sizeByte2, cancelTokenSource.Token);
                                if (cntRecv == 0) break;
                                sizeWant = sizeByte2.ToUInt16();
                                Log($"Read sizeByte2:{cntRecv}b => msgSize:{sizeWant}");
                                var buf3 = new ArraySegment<byte>(buf2, 0, sizeWant);
                                cntRecv = await socketThe.ReceiveAsync(buf3, cancelTokenSource.Token);
                                Log($"Recv {cntRecv} bytes");
                                var recvText = Encoding.UTF8.GetString(buf2, 0, cntRecv);
                                Log($"Recv '{recvText}' from '{clientThe}'");

                                sizeByte2.FromUInt16(0);
                                await socketThe.SendAsync(sizeByte2, SocketFlags.None);
                            }
                            await Task.Delay(20);
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
                            Log($"'{clientThe}' dropped");
                        }
                        catch (Exception ee)
                        {
                            Log($"'{clientThe}' {ee}");
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
        var sizeByte2 = new byte[2];
        async Task<int> SendMesssage(Socket socket, string message)
        {
            var buf2 = Encoding.UTF8.GetBytes(message);
            sizeByte2.FromUInt16((UInt16)buf2.Length);
            int sizeTxfr = await socket.SendAsync(sizeByte2, SocketFlags.None);
            if (sizeTxfr != sizeByte2.Length)
            {
                Write($"Sent size error!,");
                Write($" want={sizeByte2.Length} but real={sizeTxfr}");
                WriteLine();
                return -1;
            }

            sizeTxfr = await socket.SendAsync(buf2, SocketFlags.None);
            if (sizeTxfr != buf2.Length)
            {
                Write($"Sent message error!,");
                Write($" want={buf2.Length} but real={sizeTxfr}");
                WriteLine();
                return -1;
            }
            sizeTxfr = await socket.ReceiveAsync(sizeByte2, cancelTokenSource.Token);
            if (sizeTxfr != sizeByte2.Length)
            {
                Write($"Recv error!,");
                Write($" want={sizeByte2.Length} but real={sizeTxfr}");
                WriteLine();
                return -1;
            }

            return sizeByte2.ToUInt16();
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
                foreach (var msg in new string[]
                {
                    "Hi, how are you?",
                    "Very good!"
                })
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

static class Helper
{
    public static void FromUInt16(this byte[] bytes, UInt16 value)
    {
        bytes[0] = (byte)(value & 0xFF);
        value >>= 8;
        bytes[1] = (byte)value;
    }

    public static UInt16 ToUInt16(this byte[] bytes)
    {
        UInt16 rtn = bytes[1];
        rtn <<= 8;
        rtn += bytes[0];
        return rtn;
    }

    public static string ToHexDigit(this byte[] bytes)
    {
        var rtn = new StringBuilder();
        rtn.Append($"{bytes[0]:x2}");
        rtn.Append($".{bytes[1]:x2}");
        return rtn.ToString();
    }
}
