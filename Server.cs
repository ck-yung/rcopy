using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Server
{
    public const UInt16 CodeOfBuffer = 2;
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

        Console.WriteLine("** ** Press Ctrl-C to break.");

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

                        var byte02 = new Byte2();
                        var byte16 = new Byte16();
                        UInt16 tmp02 = 0;
                        long tmp16 = 0;
                        try
                        {
                            bool statusTxfr = false;
                            int cntTxfr = 0;
                            UInt16 sizeWant = 0;
                            var buf2 = new byte[InitBufferSize];

                            if (false == await byte02.From(CodeOfBuffer).Send(socketThe,
                                cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send the code of buffer size");
                                return;
                            }

                            long fileSizeWant = 0;
                            long fileSizeRecv = 0;
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
                                fileSizeWant = tmp16;

                                (statusTxfr, sizeWant) = await byte02.Receive(
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
                                Log.Ok($"#{idCnn} > {fileSizeWant,10} {fileTime:s} '{fileName}'");

                                if (false == await byte16.From(0).Send(socketThe,
                                    cancellationTokenSource.Token))
                                {
                                    Log.Error($"Fail to send 1st response");
                                    break;
                                }

                                int wantSize = 0;
                                fileSizeRecv = 0;
                                while (true) // (recvSize < fileSize)
                                {
                                    (statusTxfr, tmp02) = await byte02.Receive(
                                        socketThe, cancellationTokenSource.Token);
                                    if (false == statusTxfr)
                                    {
                                        Log.Error($"Read code-of-buffer failed!");
                                        break;
                                    }
                                    if (tmp02 == CodeOfBuffer)
                                    {
                                        //Log.Ok($"Recv code-of-buffer as init");
                                        wantSize = InitBufferSize;
                                    }
                                    else
                                    {
                                        (statusTxfr, tmp02) = await byte02.Receive(
                                            socketThe, cancellationTokenSource.Token);
                                        if (false == statusTxfr)
                                        {
                                            //Log.Ok($"Read buffer-size failed!");
                                            break;
                                        }
                                        //Log.Ok($"Recv buffer-size {tmp02}");
                                        wantSize = tmp02;
                                    }

                                    if (wantSize == 0) break;

                                    //Log.Ok($"dbg: Recv data [1] (wantSize:{wantSize}) ..");
                                    cntTxfr = await Helper.Recv(socketThe, buf2, wantSize,
                                        cancellationTokenSource.Token);
                                    //Log.Ok($"dbg: Recv data {cntTxfr}b");
                                    if (1 > cntTxfr) break;
                                    fileSizeRecv += cntTxfr;
                                    if (false == await byte16.From(fileSizeRecv).Send(socketThe,
                                        cancellationTokenSource.Token))
                                    {
                                        Log.Error($"Fail to send response (recvSize:{fileSizeRecv}");
                                        break;
                                    }
                                    //Log.Ok($"#{idCnn} > {fileSizeRecv,10} RSP sent");
                                }
                                //Log.Ok($"#{idCnn} > {fileSizeRecv,10} data recv");
                                cntFille += 1;
                                sumSize += fileSizeRecv;
                                //Log.Ok($"Recv data completed (want:{fileSizeWant};real:{fileSizeRecv})");
                                if (fileSizeWant!= fileSizeRecv)
                                {
                                    Log.Error($"#{idCnn} > fileSizeRecv:{fileSizeRecv}b but want:{fileSizeWant}b");
                                }

                                (statusTxfr, tmp02) = await byte02.Receive(socketThe,
                                    cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    //Log.Ok($"Read MD5 size failed!");
                                    break;
                                }
                                if (1 > tmp02)
                                {
                                    Log.Error("MD5 size is ZERO!");
                                    break;
                                }
                                cntTxfr = await Helper.Recv(socketThe, buf2, tmp02,
                                    cancellationTokenSource.Token);
                                //Log.Ok($"dbg: Recv MD5 {cntTxfr}b");
                                var md5Recv = BitConverter
                                .ToString(buf2, startIndex:0, length: tmp02)
                                .Replace("-", "")
                                .ToLower();
                                Log.Ok($"#{idCnn} MD5 '{md5Recv}' <- '{fileName}'");
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
