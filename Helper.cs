using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace rcopy;

sealed class Byte1
{
    public const int Length = 1;
    public readonly byte[] Data = new byte[1];

    public Byte1 As(byte value)
    {
        Data[0] = value;
        return this;
    }

    public byte Value()
    {
        return Data[0];
    }

    public async Task<(bool Status, byte Result)> Receive(
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

sealed class Byte2
{
    public const int Length = 2;
    public readonly byte[] Data = new byte[2];

    public Byte2 As(UInt16 uint16)
    {
        Data[0] = (byte)(uint16 & 0xFF);
        uint16 >>= 8;
        Data[1] = (byte)(uint16 & 0xFF);
        return this;
    }

    public Byte2 As(int value)
    {
        if (value > UInt16.MaxValue)
        {
            throw new OverflowException($"Cannot pack int:{value} to UInt16!");
        }
        return As((UInt16)value);
    }

    public Byte2 As(byte low, byte high)
    {
        return As(high * 256 + low);
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

    public async Task<(bool Status, byte low, byte hight)> ReceiveBytes(
        Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await Helper.Recv(socket, Data, Length, cancellation);
        if (cntTxfr != Length) return (false, 0, 0);
        return (true, Data[0], Data[1]);
    }

    public async Task<bool> Send(Socket socket, CancellationToken cancellation)
    {
        int cntTxfr = await Helper.Send(socket, Data, Length, cancellation);
        if (cntTxfr != Length) return false;
        return true;
    }
}

sealed class Byte16
{
    public readonly int Length = 16;
    public readonly byte[] Data = new byte[16];

    public Byte16 As(long value)
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

static internal class Log
{
    static string TimeText() => DateTime.Now.ToString("HH:mm:ss.fff");

    internal enum LogType
    {
        None,
        Stdout,
        Stderr,
        Debugger,
    }

    static void LogNothing(bool isTimestamp, string format, params object[] args) { }

    static void LogToStdout(bool isTimestamp, string format, params object[] args)
    {
        try
        {
            var message = string.Format(format, args);
            if (isTimestamp)
            {
                Console.WriteLine(TimeText()+ " "+ message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
        catch
        {
            // donting ..
        }
    }

    static void LogToStderr(bool isTimestamp, string format, params object[] args)
    {
        try
        {
            var message = string.Format(format, args);
            if (isTimestamp)
            {
                Console.Error.WriteLine(TimeText() + " " + message);
            }
            else
            {
                Console.Error.WriteLine(message);
            }
        }
        catch
        {
            // donting ..
        }
    }

    internal static LogType OkTo { get; private set; } = LogType.Stdout;
    public static void Ok(string format, params object[] args)
    {
        if (OkTo == LogType.Stdout)
        {
            LogToStdout(false, format, args);
        }
        else if (OkTo == LogType.Stderr)
        {
            LogToStderr(false, format, args);
        }
    }

    public static void Error(string message)
    {
        Console.Error.WriteLine(
            $"Error: {TimeText()} {message}");
    }

    internal static LogType DebugTo { get; private set; } = LogType.None;
    public static void Debug(string format, params object[] args)
    {
        if (DebugTo == LogType.Stderr)
        {
            LogToStderr(true, format, args);
        }
        else if (OkTo == LogType.Debugger)
        {
            var message = string.Format(format, args);
            System.Diagnostics.Debug.WriteLine(
                $"{TimeText()} {message}");
        }
    }

    internal static LogType VerboseTo { get; private set; } = LogType.None;
    public static void Verbose(string format, params object[] args)
    {
        if (VerboseTo == LogType.Stderr)
        {
            LogToStderr(true, format, args);
        }
    }

    public static void Init()
    {
        if (System.Diagnostics.Debugger.IsAttached)
        {
            DebugTo = LogType.Debugger;
        }
        else
        {
            var tmp2 = Environment.GetEnvironmentVariable(nameof(rcopy));
            if (tmp2?.Contains("debug:on") ?? false)
            {
                DebugTo = LogType.Stderr;
            }
        }
    }

    public static void VerboseSwitch(bool flag)
    {
        VerboseTo = flag ? LogType.Stderr : LogType.None;
    }
}

static partial class Helper
{
    public const byte DefaultCodeOfBufferSize = 2;  // 16K

    public static int GetBufferSize(byte code)
    {
        switch (code)
        {
            case 1:
                return 0x2000; // 8K
            case 2:
                return 0x4000; // 16K
            case 3:
                return 0x8000; // 32K
            case 4:
                return 0x10000; // 64K
            default:
                throw new ArgumentOutOfRangeException(
                    $"Invalid CodeOfBuffer:{code} is found!");

        }
    }

    static public bool PrintSyntax(bool isDetailed = false)
    {
        Console.WriteLine($"""
            Syntax:
              {nameof(rcopy)} to HOST:PORT FILE
            
            Syntax:
              {nameof(rcopy)} on HOST:PORT [--out-dir OUT-DIR] [OPTIONS]
            """);
        if (false == isDetailed)
        {
            Console.WriteLine($"""

                Syntax:
                  {nameof(rcopy)} --help
                """);
            return false;
        }
        Console.WriteLine("""

              where:
              HOST is 'localhost', an IPv4, or, a DNS host name
            
            OPTIONS:
              NAME          DEFAULT  ALTER
              --keep-dir    on       off
              --verbose     off      on
              --buffer-size 2        1  3  4
              --allow       all      localhost | 192.168.0.* | =.=.=.* | 192.168.0.2
            where
              BufferSize:  1=8K, 2=16K, 3=32K, 4=64K
              'localhost'  at '--allow' to allow connecting at '127.0.0.1' or '[::1]'.
              Star sign *  at '--allow' to allow any digit of remote IP at the corresponding position.
              Equal sign = at '--allow' to only allow same digit of local IP at the corresponding position.
            """);
        return false;
    }

    public const byte INIT_CTRL_CODE = 0x10;

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
        return rtn;
    }

    public static bool Compare(this byte[] the, byte[] other, int length = -1)
    {
        if (length < 0) length = the.Length;
        for (int ii = 0; ii < length; ii++)
            if (the[ii] != other[ii]) return false;
        return true;
    }

    public static async Task<int> Read(Stream stream,
        byte[] data, int wantSize, CancellationToken token)
    {
        int rtn = 0;
        int offset = 0;
        int wantSize2 = wantSize;
        int cntTxfr;
        while (0 < wantSize2)
        {
            cntTxfr = await stream.ReadAsync(data, offset, wantSize2, token);
            if (cntTxfr < 1) break;
            rtn += cntTxfr;
            offset += cntTxfr;
            wantSize2 -= cntTxfr;
        }
        return rtn;
    }

    public static async Task<int> Write(Stream stream,
        byte[] data, int wantSize, CancellationToken token)
    {
        if (wantSize < 1) return 0;
        await stream.WriteAsync(data, 0, wantSize, token);
        return wantSize;
    }

    const string ipV4Local = "127.0.0.1";
    const string ipV6Local = "::1";
    const string localHost = "localhost";

    /// <summary>
    /// Make a comparer of two IP whose formats are
    ///  "999.999.999.999" or "999.999.999:8888"
    /// </summary>
    /// <param name="mask">e.g. "=.=.=.*", "1.2.3,*", "localhost", "all"</param>
    /// <remarks>
    ///  MASK     | Arg1      Arg2      | Compare
    ///  ----     | ----      ----      | -------
    ///  all      | any       any       | true
    ///
    ///  localhost| any       127.0.0.1 | true
    ///           | any       ::1       | true
    ///           | any       other     | false
    ///
    ///  =.=.=.*  | 1.2.3.4   1.2.3.5   | true
    ///           | 1.2.3.4   1.2.6.7   | false
    ///
    ///  =.=.*.*  | 1.2.3.4   1.2.5.6   | true
    ///           | 1.2.3.4   1.5.6.7   | false
    ///
    ///  1.2.3.*  | any       1.2.3.4   | true
    ///           | any       1.5.3.6   | false
    /// </remarks>
    /// <returns>Compare(LocalIp, RemoteIp), or,
    /// null if param "mask" is in wrong format</returns>
    public static Func<string, string, bool>
        MakeIpMask(string mask, string optName)
    {
        if ("all" == mask) return (_, _) => true;

        if ("localhost" == mask) return (_, arg)
                => arg.StartsWith("127.0.0.1:") || arg.StartsWith("[::1]:");

        Func<string, Func<string, string, bool>> txtToFunc = (arg) =>
        {
            switch(arg)
            {
                case "=": return (a2, b2) => a2 == b2;
                case "*": return (_, _) => true;
                default: return (_, c2) => arg == c2;
            }
        };

        var mm = mask
            .Split('.', count: 5)
            .Select((it) => txtToFunc(it))
            .ToArray();

        if (mm.Length != 4)
        {
            throw new ArgumentException(
                $"'{mask}' is invalid to '{optName}'");
        }

        return (ipLocal, ipRemote) =>
        {
            Log.Debug("IpMask(local='{0}',remote='{1}')", ipLocal, ipRemote);
            if (ipLocal.StartsWith("[::1]:")) ipLocal = "127.0.0.1:1";
            if (ipRemote.StartsWith("[::1]:")) ipLocal = "127.0.0.1:2";
            var aa = (ipLocal
            .Split(':', count: 2)
            .FirstOrDefault() ?? string.Empty)
            .Split('.', count: 5)
            .ToArray();

            var bb = (ipRemote
            .Split(':', count: 2)
            .FirstOrDefault() ?? string.Empty)
            .Split('.', count: 5)
            .ToArray();

            if (aa.Length != 4 || bb.Length != 4) return false;

            return aa
            .Zip(bb)
            .Zip(mm)
            .All((it) => it.Second(it.First.First, it.First.Second));
        };
    }
}

internal class Buffer
{
    bool flag;
    byte[] bufferA;
    byte[] bufferB;

    public Buffer(int size)
    {
        flag = false;
        bufferA = new byte[size];
        bufferB = new byte[size];
    }

    public byte[] InputData()
    {
        return flag ? bufferA : bufferB;
    }

    public byte[] OutputData()
    {
        return flag ? bufferB : bufferA;
    }

    public bool Switch()
    {
        flag = !flag;
        return flag;
    }
}

internal class OpenFile
{
    public Stream Stream { get; init; }
    Action<Stream> CloseImpl { get; set; } = (it) => it.Close();

    public OpenFile(string path, bool isNew = false)
    {
        if ("-" == path)
        {
            CloseImpl = (_) => { };
            Stream = isNew
                ? Console.OpenStandardOutput()
                : Console.OpenStandardInput();
        }
        else
        {
            if (isNew)
            {
                Stream = File.Create(path);
            }
            else
            {
                Stream = File.OpenRead(path);
            }
        }
    }

    public void Close()
    {
        CloseImpl(Stream);
    }
}
