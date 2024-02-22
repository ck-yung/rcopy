using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace rcopy2;

sealed class Byte2
{
    public const int Length = 2;
    public readonly byte[] Data = new byte[2];

    public void From(UInt16 uint16)
    {
        Data[0] = (byte)(uint16 & 0xFF);
        uint16 >>= 8;
        Data[1] = (byte)(uint16 & 0xFF);
    }

    public void From(int value)
    {
        if (value > UInt16.MaxValue)
        {
            throw new OverflowException($"Cannot pack int:{value} to UInt16!");
        }
        UInt16 uint16 = (UInt16)value;
        Data[0] = (byte)(uint16 & 0xFF);
        uint16 >>= 8;
        Data[1] = (byte)(uint16 & 0xFF);
    }

    public UInt16 Value()
    {
        UInt16 rtn = Data[1];
        rtn <<= 8;
        rtn += Data[0];
        return rtn;
    }

    public async Task<(bool Status, UInt16 Result)> Receive(
        Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await socket.ReceiveAsync(Data, cancellation);
        switch (cntTxfr)
        {
            case Length:
                return (true, Value());
            case 1:
                cntTxfr = socket.Receive(Data, offset: 1, size: 1,
                    socketFlags: SocketFlags.None);
                if (1 == cntTxfr) return(true, Value());
                break;
            default:
                break;
        }
        return (false, 0);
    }

    public async Task<bool> Send(Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await socket.SendAsync(Data, cancellation);
        switch (cntTxfr)
        {
            case Length:
                return true;
            case 1:
                cntTxfr = socket.Send(Data, offset: 1, size: 1,
                    socketFlags: SocketFlags.None);
                if (1 == cntTxfr) return true;
                break;
            default:
                break;
        }
        return false;
    }
}

sealed class Byte8
{
    public readonly int Length = 8;
    public readonly byte[] Data = new byte[8];

    public void From(long value)
    {
        Data[0] = (byte)(value & 0xFF);
        value >>= 8;
        Data[1] = (byte)(value & 0xFF);
        value >>= 8;
        Data[2] = (byte)(value & 0xFF);
        value >>= 8;
        Data[3] = (byte)(value & 0xFF);
        value >>= 8;
        Data[4] = (byte)(value & 0xFF);
        value >>= 8;
        Data[5] = (byte)(value & 0xFF);
        value >>= 8;
        Data[6] = (byte)(value & 0xFF);
        value >>= 8;
        Data[7] = (byte)(value & 0xFF);
    }

    public long Value()
    {
        long rtn = Data[7];
        rtn <<= 8;
        rtn += Data[6];
        rtn <<= 8;
        rtn += Data[5];
        rtn <<= 8;
        rtn += Data[4];
        rtn <<= 8;
        rtn += Data[3];
        rtn <<= 8;
        rtn += Data[2];
        rtn <<= 8;
        rtn += Data[1];
        rtn <<= 8;
        rtn += Data[0];
        return rtn;
    }
}

sealed class ClientQueue
{
    Queue<Socket> Queue = new();
    object Lock = new();

    public void Add(Socket socket)
    {
        lock (Lock)
        {
            Queue.Enqueue(socket);
        }
    }

    public Socket? Get()
    {
        lock (Lock)
        {
            if (Queue.Count == 0) return null;
            return Queue.Dequeue();
        }
    }
}

static partial class Helper
{
    public static void Log(string message)
    {
        Console.WriteLine(
            $"{DateTime.Now.ToString("s")} {message}");
    }

    public static void ErrorLog(string message)
    {
        Console.Error.WriteLine(
            $"{DateTime.Now.ToString("s")} {message}");
    }

    [GeneratedRegex(
    @"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})$")]
    private static partial Regex RegexPatternIpPort();

    [GeneratedRegex(@"^(?<host>[\D][^\s].*):(?<port>\d{1,5})$")]
    private static partial Regex RegexPatternHostPort();

    public static IPEndPoint ParseIpEndpoint(string arg)
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

    public static string ToHexDigit(this byte[] bytes)
    {
        var length = int.Min(bytes.Length, 8);
        var rtn = new StringBuilder();
        rtn.Append($"{bytes[0]:x2}");
        for ( int ii = 1; ii < length; ii+=1)
        {
            rtn.Append($".{bytes[ii]:x2}");
        }
        return rtn.ToString();
    }
}
