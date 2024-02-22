using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Client
{
    public static async Task<bool> Run(string ipServer, Info[] infos)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        int cntTxfr = 0;
        bool statusTxfr = false;
        UInt16 response = 0;
        var sizeThe = new Byte2();
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

        async Task<int> SendFileInfo(Socket socket, Info info)
        {
            var tmp3 = new Byte16();

            long tmp2 = new DateTimeOffset(info.File.LastWriteTimeUtc).ToUnixTimeSeconds();
            if (false == await tmp3.From(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                ErrorLog($"Fail to send date-time");
                return -1;
            }

            tmp2 = info.File.Length;
            if (false == await tmp3.From(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                ErrorLog($"Fail to send file-size");
                return -1;
            }

            var buf2 = Encoding.UTF8.GetBytes(info.Name);
            sizeThe.From(buf2.Length);
            if (false == await sizeThe.Send(socket, cancellationTokenSource.Token))
            {
                ErrorLog($"Fail to send name-size");
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

        var endPointThe = ParseIpEndpoint(ipServer);
        var connectTimeout = new CancellationTokenSource(millisecondsDelay: 3000);
        Log($"Connect {endPointThe.Address} at port {endPointThe.Port} ..");
        using (var serverThe = new TcpClient())
        {
            try
            {
                await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
                Log("Connected");
                var socketThe = serverThe.Client;

                (statusTxfr, response) = await sizeThe.Receive(socketThe,
                    cancellationTokenSource.Token);
                if (false == statusTxfr)
                {
                    ErrorLog($"Fail to read buffer size");
                    return false;
                }
                Log($"Code of buffer size is {response}");

                int rsp = 0;
                var infoLong = new Byte8();
                foreach (var info in infos)
                {
                    rsp = await SendFileInfo(socketThe, info);
                    if (rsp != 0)
                    {
                        ErrorLog($"Recv unknown response {rsp}");
                        break;
                    }
                }
                serverThe.Client.Shutdown(SocketShutdown.Both);
                Task.Delay(20).Wait();
                serverThe.Client.Close();
                Log("Connection is closed");
                Task.Delay(20).Wait();
            }
            catch (SocketException se)
            {
                ErrorLog($"Network: {se.Message}");
            }
            catch (Exception ee)
            {
                ErrorLog($"Error! {ee}");
            }
        }
        return true;
    }
}

