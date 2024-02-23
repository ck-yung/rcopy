using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;
namespace rcopy2;

static class Client
{
    public static async Task<int> Run(string ipServer, IEnumerable<Info> infos)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        int cntTxfr = 0;
        bool statusTxfr = false;
        UInt16 response = 0;
        var sizeThe = new Byte2();
        long tmp16 = 0;
        var buf2 = new byte[4096];

        /*
        async Task<int> SendMesssage(Socket socket, string message)
        {
            var buf2 = Encoding.UTF8.GetBytes(message);

            sizeThe.From(buf2.Length);
            if (false == await sizeThe.Send(socket, cancellationTokenSource.Token))
            {
                ErrorLog($"Fail to send msg-size");
                return -1;
            }

            cntTxfr = await socket.SendAsync(buf2, SocketFlags.None);
            if (cntTxfr != buf2.Length)
            {
                ErrorLog($"Sent message error!, want={buf2.Length} but real={cntTxfr}");
                return -1;
            }

            (statusTxfr, response) = await sizeThe.Receive(socket,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                ErrorLog($"Fail to read response");
                return -1;
            }
            // TODO: Check if reponse is ZERO

            return sizeThe.Value();
        }
        */

        async Task<bool> SendFileInfo(Socket socket, Info info)
        {
            var byte16 = new Byte16();

            long tmp2 = new DateTimeOffset(info.File.LastWriteTimeUtc).ToUnixTimeSeconds();
            if (false == await byte16.From(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send date-time");
                return false;
            }

            tmp2 = info.File.Length;
            if (false == await byte16.From(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send file-size");
                return false;
            }

            var buf2 = Encoding.UTF8.GetBytes(info.Name);
            sizeThe.From(buf2.Length);
            if (false == await sizeThe.Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send name-size");
                return false;
            }

            cntTxfr = await Send(socket, buf2, buf2.Length,
                cancellationTokenSource.Token);
            if (cntTxfr != buf2.Length)
            {
                Log.Error($"Sent message error!, want={buf2.Length} but real={cntTxfr}");
                return false;
            }

            (statusTxfr, tmp16) = await byte16.Receive(socket,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to read response");
                return false;
            }
            if (0!=tmp16)
            {
                Log.Ok($"RSP < {tmp16}");
            }
            return (tmp16 == 0);
        }

        int cntFile = 0;
        long sumSize = 0;
        var endPointThe = ParseIpEndpoint(ipServer);
        var connectTimeout = new CancellationTokenSource(millisecondsDelay: 3000);
        Log.Ok($"Connect {endPointThe.Address} at port {endPointThe.Port} ..");
        using var serverThe = new TcpClient();
        try
        {
            await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
            Log.Ok("Connected");
            var socketThe = serverThe.Client;

            (statusTxfr, response) = await sizeThe.Receive(socketThe,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to read buffer size");
                return -1;
            }
            Log.Ok($"Code of buffer size is {response}");

            var infoLong = new Byte8();
            foreach (var info in infos)
            {
                if (info.File.Length < 1)
                {
                    Log.Ok($"Skip empty file '{info.Name}'");
                    continue;
                }
                if (false == await SendFileInfo(socketThe, info))
                {
                    break;
                }
                cntFile += 1;
                sumSize += info.File.Length;
            }
            serverThe.Client.Shutdown(SocketShutdown.Both);
            Task.Delay(20).Wait();
            serverThe.Client.Close();
            Log.Ok($"Connection is closed (cntFile={cntFile}; sumSize={sumSize})");
            Task.Delay(20).Wait();
        }
        catch (SocketException se)
        {
            Log.Error($"Network: {se.Message}");
        }
        catch (Exception ee)
        {
            Log.Error($"Error! {ee}");
        }
        return cntFile;
    }
}
