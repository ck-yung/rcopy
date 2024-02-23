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
        Log.Ok($"Start listen on {endPointThe.Address} at {endPointThe.Port}");
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
                var buf3 = new byte[2048];
                while (true)
                {
                    var clSocket = await listener.AcceptSocketAsync(
                        cancellationTokenSource.Token);
                    socketQueue.Add(clSocket);

                    _ = Task.Run(async () =>
                    {
                        (var idCnn, var socketThe) = socketQueue.Get();
                        if (socketThe == null)
                        {
                            Log.Error("Error! No client connection is found!");
                            return;
                        }

                        var remoteEndPoint = socketThe.RemoteEndPoint;
                        Log.Ok($"#{idCnn} '{remoteEndPoint}' connected");

                        int cntFille = 0;
                        long sumSize = 0;

                        var sizeThe = new Byte2();
                        var byte16 = new Byte16();
                        long tmp16 = 0;
                        try
                        {
                            bool statusTxfr = false;
                            int cntTxfr = 0;
                            UInt16 sizeWant = 0;
                            var buf2 = new byte[InitBufferSize];

                            if (false == await sizeThe.From(2).Send(socketThe,
                                cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send the code of buffer size");
                                return;
                            }

                            while (true)
                            {
                                (statusTxfr, tmp16) = await byte16.Receive(clSocket,
                                    cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    break;
                                }
                                DateTimeOffset fileTime = DateTimeOffset.FromUnixTimeSeconds(tmp16);

                                (statusTxfr, tmp16) = await byte16.Receive(clSocket,
                                    cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    Log.Ok($"Read file-size failed!");
                                    break;
                                }
                                long fileSize = tmp16;

                                (statusTxfr, sizeWant) = await sizeThe.Receive(
                                    socketThe, cancellationTokenSource.Token);
                                if ((false == statusTxfr) || (sizeWant == 0))
                                {
                                    Log.Ok($"Read length-of-file-name failed!");
                                    break;
                                }

                                System.Diagnostics.Debug.WriteLine(
                                    $"Read length-of-file-name:{sizeWant}");
                                cntTxfr = await Helper.Recv(socketThe, buf3,
                                    sizeWant, cancellationTokenSource.Token);
                                if (cntTxfr != sizeWant)
                                {
                                    Log.Ok($"Read file-name failed! (want:{sizeWant} but real:{cntTxfr})");
                                    break;
                                }
                                System.Diagnostics.Debug.WriteLine(
                                    $"{DateTime.Now.ToString("s")} Recv {cntTxfr} bytes");
                                var fileName = Encoding.UTF8.GetString(buf3, 0, cntTxfr);
                                Log.Ok($"#{idCnn} > {fileSize,10} {fileTime:s} '{fileName}'");

                                if (false == await byte16.From(0).Send(socketThe,
                                    cancellationTokenSource.Token))
                                {
                                    Log.Error($"Fail to send 1st response");
                                    break;
                                }

                                long recvSize = 0;
                                int wantSize = 0;
                                while (recvSize < fileSize)
                                {
                                    wantSize = InitBufferSize;
                                    if ((recvSize + wantSize) > fileSize)
                                    {
                                        wantSize = (int) (fileSize - recvSize);
                                    }
                                    Log.Ok($"dbg: Recv data [1] (wantSize:{wantSize}) ..");
                                    cntTxfr = await Helper.Recv(socketThe, buf2, wantSize,
                                        cancellationTokenSource.Token);
                                    Log.Ok($"dbg: Recv data {cntTxfr}b");
                                    if (1 > cntTxfr) break;
                                    recvSize += cntTxfr;
                                    if (false == await byte16.From(recvSize).Send(socketThe,
                                        cancellationTokenSource.Token))
                                    {
                                        Log.Error($"Fail to send response (recvSize:{recvSize}");
                                        break;
                                    }
                                    Log.Ok($"#{idCnn} > {recvSize,10} RSP sent");
                                }
                                Log.Ok($"#{idCnn} > {recvSize,10} data recv");
                                cntFille += 1;
                                sumSize += recvSize;
                            }
                        }
                        catch (Exception ee)
                        {
                            Log.Error($"#{idCnn} '{remoteEndPoint}' {ee}");
                        }
                        finally
                        {
                            Log.Ok($"#{idCnn} '{remoteEndPoint}' dropped (cntFile:{cntFille}; sumSize:{sumSize})");
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
                Log.Ok("Listening is stopped");
            }
            catch (Exception ee)
            {
                Log.Error("Accept: " + ee.GetType().Name + ": " + ee.Message);
            }

            try
            {
                await Task.Delay(20);
                listener.Stop();
                await Task.Delay(20);
            }
            catch (Exception ee)
            {
                Log.Error(ee.GetType().Name + ": " + ee.Message);
            }
        }).Wait();

        Task.Delay(20).Wait();
        return true;
    }
}
