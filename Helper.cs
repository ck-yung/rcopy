using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace rcopy2;

record Info(string Name, FileInfo File);

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

    public Byte2 From(int value)
    {
        if (value > UInt16.MaxValue)
        {
            throw new OverflowException($"Cannot pack int:{value} to UInt16!");
        }
        UInt16 uint16 = (UInt16)value;
        Data[0] = (byte)(uint16 & 0xFF);
        uint16 >>= 8;
        Data[1] = (byte)(uint16 & 0xFF);
        return this;
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
        int cntTxfr = await Helper.Recv(socket, Data, Length, cancellation);
        if (cntTxfr != Length) return (false, 0);
        return (true, Value());
    }

    public async Task<bool> Send(Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await Helper.Send(socket, Data, Length, cancellation);
        if (cntTxfr != Length) return false;
        return true;
    }
}

sealed class Byte8
{
    public readonly int Length = 8;
    public readonly byte[] Data = new byte[8];

    public Byte8 From(int value)
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
        return this;
    }

    public int Value()
    {
        int rtn = Data[7];
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

sealed class Byte16
{
    public readonly int Length = 16;
    public readonly byte[] Data = new byte[16];

    public Byte16 From(long value)
    {
        Data[0] = (byte)(value & 0xFF);
        for (int ii = 1; ii < 16; ii += 1)
        {
            value >>= 8;
            Data[ii] = (byte)(value & 0xFF);
        }
        return this;
    }

    public long Value()
    {
        long rtn = Data[15];
        for (int ii = 14; 0 <= ii; ii -=1)
        {
            rtn <<= 8;
            rtn += Data[ii];
        }
        return rtn;
    }

    public async Task<(bool Status, long Result)> Receive(
        Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await Helper.Recv(socket, Data, Length, cancellation);
        if (cntTxfr != Length) return (false, 0);
        return (true, Value());
    }

    public async Task<bool> Send(Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await Helper.Send(socket, Data, Length, cancellation);
        if (cntTxfr != Length) return false;
        return true;
    }
}

sealed class ClientQueue
{
    Queue<(int,Socket)> Queue = new();
    int AddCount = 0;

    public int Add(Socket socket)
    {
        lock (this)
        {
            AddCount++;
            Queue.Enqueue((AddCount,socket));
            return AddCount;
        }
    }

    public (int, Socket?) Get()
    {
        lock (this)
        {
            if (Queue.Count == 0) return (0, null);
            return Queue.Dequeue();
        }
    }
}

static class Log
{
    public static void Ok(string message)
    {
        Console.WriteLine(
            $"{DateTime.Now.ToString("s")} {message}");
    }

    public static void Error(string message)
    {
        Console.Error.WriteLine(
            $"{DateTime.Now.ToString("s")} {message}");
    }
}

static partial class Helper
{
    public const int InitBufferSize = 16 * 1024;

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

    public static async Task<int> Recv(Socket socket,
        byte[] data, int wantSize, CancellationToken token)
    {
        int rtn = 0;
        int offset = 0;
        int wantSize2 = wantSize;
        int cntTxfr;
        Memory<byte> buffer;
        while (0 < wantSize2)
        {
            buffer = new Memory<byte>(data, start: offset, length: wantSize2);
            cntTxfr = await socket.ReceiveAsync(buffer, token);
            if (cntTxfr < 1) break;
            rtn += cntTxfr;
            offset += cntTxfr;
            wantSize2 -= cntTxfr;
        }
        //Log.Ok($"Helper.Recv {rtn}b (want:{wantSize})");
        return rtn;
    }

    public static async Task<int> Send(Socket socket,
        byte[] data, int wantSize, CancellationToken token)
    {
        int rtn = 0;
        int offset = 0;
        int wantSize2 = wantSize;
        int cntTxfr;
        Memory<byte> buffer;
        while (0 < wantSize2)
        {
            buffer = new Memory<byte>(data, start: offset, length: wantSize2);
            cntTxfr = await socket.SendAsync(buffer, token);
            if (cntTxfr < 1) break;
            rtn += cntTxfr;
            offset += cntTxfr;
            wantSize2 -= cntTxfr;
        }
        //Log.Ok($"Helper.Send {rtn}b (want:{wantSize})");
        return rtn;
    }
}
