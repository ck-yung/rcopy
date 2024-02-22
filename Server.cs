using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Server
{
    public static bool Run(IEnumerable<string> mainArgs,
        CancellationTokenSource cancellationTokenSource)
    {
        var ipServerText = mainArgs.First();
        var endPointThe = ParseIpEndpoint(ipServerText);
        var listener = new TcpListener(endPointThe);
        Log($"Start listen on {endPointThe.Address} at port {endPointThe.Port}");
        listener.Start();
        Console.CancelKeyPress += (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC
            || e.SpecialKey == ConsoleSpecialKey.ControlBreak)
            {
                cancellationTokenSource.Cancel();
                Task.Delay(100).Wait();
            }
        };

        Console.WriteLine("*** Press Ctrl-C to break.");

        Task.Run(async () =>
        {
            try
            {
                var socketQueue = new ClientQueue();
                while (true)
                {
                    var clSocket = await listener.AcceptSocketAsync(
                        cancellationTokenSource.Token);
                    socketQueue.Add(clSocket);
                    _ = Task.Run(async () =>
                    {
                        var socketThe = socketQueue.Get();
                        if (socketThe == null)
                        {
                            ErrorLog("Error! No client connection is found!");
                            return;
                        }
                        var remoteEndPoint = socketThe.RemoteEndPoint;
                        Log($"'{remoteEndPoint}' connected");

                        bool statusTxfr = false;
                        int cntTxfr = 0;
                        UInt16 sizeWant = 0;
                        var sizeThe = new Byte2();
                        var buf2 = new byte[4096];
                        try
                        {
                            while (true)
                            {
                                (statusTxfr, sizeWant) = await sizeThe.Receive(
                                    socketThe, cancellationTokenSource.Token);
                                if ((false == statusTxfr) || (sizeWant == 0))
                                {
                                    break;
                                }

                                System.Diagnostics.Debug.WriteLine(
                                    $"Read msgSize:{sizeWant}");
                                var buf3 = new ArraySegment<byte>(buf2, 0, sizeWant);
                                cntTxfr = await socketThe.ReceiveAsync(buf3,
                                    cancellationTokenSource.Token);
                                System.Diagnostics.Debug.WriteLine(
                                    $"{DateTime.Now.ToString("s")} Recv {cntTxfr} bytes");
                                var recvText = Encoding.UTF8.GetString(buf2, 0, cntTxfr);
                                Log($"'{remoteEndPoint}' > '{recvText}'");

                                sizeThe.From(0);
                                if (false == await sizeThe.Send(socketThe, cancellationTokenSource.Token))
                                {
                                    ErrorLog($"Fail to send response");
                                    break;
                                }
                            }
                            Log($"'{endPointThe}' dropped");
                            await Task.Delay(20);
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
                        }
                        catch (Exception ee)
                        {
                            ErrorLog($"'{endPointThe}' {ee}");
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
                ErrorLog("Accept: " + ee.GetType().Name + ": " + ee.Message);
            }

            try
            {
                await Task.Delay(20);
                listener.Stop();
                await Task.Delay(20);
            }
            catch (Exception ee)
            {
                ErrorLog(ee.GetType().Name + ": " + ee.Message);
            }
        }).Wait();

        Task.Delay(20).Wait();
        return true;
    }
}
