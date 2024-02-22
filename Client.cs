using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Client
{
    public static async Task<bool> Run(IEnumerable<string> mainArgs,
        CancellationTokenSource cancellationTokenSource)
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

        var ipTarget = mainArgs.First();
        var endPointThe = ParseIpEndpoint(ipTarget);
        var connectTimeout = new CancellationTokenSource(millisecondsDelay: 3000);
        Log($"Connect {endPointThe.Address} at port {endPointThe.Port} ..");
        using (var serverThe = new TcpClient())
        {
            try
            {
                await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
                Log("Connected");
                var socketThe = serverThe.Client;
                int rsp = 0;
                foreach (var msg in mainArgs.Skip(1).Union(["Bye!"]))
                {
                    rsp = await SendMesssage(socketThe, msg);
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

