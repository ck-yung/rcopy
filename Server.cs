using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Server
{
    public static bool Run(string ipServer, IEnumerable<string> args)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        var endPointThe = ParseIpEndpoint(ipServer);
        var listener = new TcpListener(endPointThe);
        Log($"Start listen on {endPointThe.Address} at {endPointThe.Port}");
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

                        var sizeThe = new Byte2();
                        var tmp3 = new Byte16();
                        long tmp4 = 0;
                        try
                        {
                            bool statusTxfr = false;
                            int cntTxfr = 0;
                            UInt16 sizeWant = 0;
                            var buf2 = new byte[InitBufferSize];

                            if (false == await sizeThe.From(1).Send(socketThe,
                                cancellationTokenSource.Token))
                            {
                                ErrorLog($"Fail to send the code of buffer size");
                                return;
                            }

                            while (true)
                            {
                                (statusTxfr, tmp4) = await tmp3.Receive(clSocket,
                                    cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    break;
                                }
                                DateTimeOffset fileTime = DateTimeOffset.FromUnixTimeSeconds(tmp4);

                                (statusTxfr, tmp4) = await tmp3.Receive(clSocket,
                                    cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    Log($"Read file-size failed!");
                                    break;
                                }
                                long fileSize = tmp4;

                                (statusTxfr, sizeWant) = await sizeThe.Receive(
                                    socketThe, cancellationTokenSource.Token);
                                if ((false == statusTxfr) || (sizeWant == 0))
                                {
                                    Log($"Read length-of-file-name failed!");
                                    break;
                                }

                                System.Diagnostics.Debug.WriteLine(
                                    $"Read msgSize:{sizeWant}");
                                var buf3 = new ArraySegment<byte>(buf2, 0, sizeWant);
                                cntTxfr = await socketThe.ReceiveAsync(buf3,
                                    cancellationTokenSource.Token);
                                System.Diagnostics.Debug.WriteLine(
                                    $"{DateTime.Now.ToString("s")} Recv {cntTxfr} bytes");
                                var fileName = Encoding.UTF8.GetString(buf2, 0, cntTxfr);
                                Log($"'{remoteEndPoint}' > {fileSize,10} {fileTime:s} '{fileName}'");

                                if (false == await sizeThe.From(0).Send(socketThe,
                                    cancellationTokenSource.Token))
                                {
                                    ErrorLog($"Fail to send response");
                                    break;
                                }
                            }
                        }
                        catch (Exception ee)
                        {
                            ErrorLog($"'{remoteEndPoint}' {ee}");
                        }
                        finally
                        {
                            Log($"'{remoteEndPoint}' dropped");
                            await Task.Delay(20);
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
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
