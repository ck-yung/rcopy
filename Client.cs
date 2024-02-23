using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;
namespace rcopy2;

static class Client
{
    public static async Task<int> Run(string ipServer, IEnumerable<Info> infos)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        bool statusTxfr = false;
        var byte02 = new Byte2();
        var byte16 = new Byte16();
        var buf2 = new byte[InitBufferSize];

        async Task<int> SendFileInfo(Socket socket, Info info)
        {
            var byte16 = new Byte16();

            long tmp2 = new DateTimeOffset(info.File.LastWriteTimeUtc).ToUnixTimeSeconds();
            if (false == await byte16.As(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send date-time");
                return 0;
            }

            tmp2 = info.File.Length;
            if (false == await byte16.As(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send file-size");
                return 0;
            }

            var buf2 = Encoding.UTF8.GetBytes(info.Name);
            byte02.As(buf2.Length);
            if (false == await byte02.Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send name-size");
                return 0;
            }

            int cntTxfr = await Send(socket, buf2, buf2.Length,
                cancellationTokenSource.Token);
            if (cntTxfr != buf2.Length)
            {
                Log.Error($"Sent message error!, want={buf2.Length} but real={cntTxfr}");
                return 0;
            }

            (statusTxfr, var rsp16) = await byte16.Receive(socket,
                cancellationTokenSource.Token);
            if ((false == statusTxfr) || (rsp16!=0))
            {
                Log.Error($"Error info response (status:{statusTxfr};RSP:{rsp16})");
                return 0;
            }
            return cntTxfr + 32;
        }

        Socket socketThe;
        long sentSizeThe = 0;
        int wantSize = 0;
        int readRealSize = 0;
        UInt16 codeOfBuffer = 0;
        async Task<int> SendAndGetResponse()
        {
            if (readRealSize == InitBufferSize)
            {
                if (false == await byte02.As(codeOfBuffer).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send init code!");
                    return 0;
                }
                //Log.Ok("Send codeOfBuffer ok");
            }
            else
            {
                if (false == await byte02.As(0).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send last init code!");
                    return 0;
                }
                if (false == await byte02.As(readRealSize).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send last data-size {readRealSize}b");
                    return 0;
                }
                //Log.Ok($"Send last buffer size {readRealSize} ok");
            }

            var sendTask = Helper.Send(socketThe, buf2, readRealSize,
                cancellationTokenSource.Token);
            sendTask.Wait();
            int cntTxfr = sendTask.Result;
            //Log.Ok($"Send buffer real:{cntTxfr}b; want:{readRealSize}b [sentSizeThe:{sentSizeThe}]");

            if (1 > cntTxfr) return 0;
            sentSizeThe += cntTxfr;
            (statusTxfr, var rsp16) = await byte16.Receive(socketThe,
                cancellationTokenSource.Token);
            if ((false == statusTxfr) || (rsp16 != sentSizeThe))
            {
                Log.Error($"RSP to SendAndGetResponse: (status:{statusTxfr}; RSP:{rsp16} but want:{sentSizeThe})");
            }
            //else Log.Ok($"RSP ok {rsp16}");
            return cntTxfr;
        }

        int cntFile = 0;
        long sumSize = 0;
        long sumSent = 0;
        var endPointThe = ParseIpEndpoint(ipServer);
        var connectTimeout = new CancellationTokenSource(millisecondsDelay: 3000);
        Log.Ok($"Connect {endPointThe.Address} at port {endPointThe.Port} ..");
        using var serverThe = new TcpClient();
        try
        {
            await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
            Log.Ok("Connected");
            socketThe = serverThe.Client;

            (statusTxfr, var controlCode) = await byte02.Receive(socketThe,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to recv control code!");
                return -1;
            }
            var upperControlFlag = (controlCode & 0xFF00);
            var md5Flag = (upperControlFlag & Md5Required) == Md5Required;
            //Log.Ok($"Upper Control:{upperControlFlag:x4}, Md5:{md5Flag}");
            codeOfBuffer = (UInt16)(controlCode & (UInt16) 0x00FF);

            if (false == await byte02.As(controlCode).Send(socketThe, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to reply control code!");
                return -1;
            }

            foreach (var info in infos)
            {
                if (0 == await SendFileInfo(socketThe, info))
                {
                    break;
                }

                Task<int> readTask;
                Task<int> sendTask;
                sentSizeThe = 0;
                try
                {
                    var md5 = Md5Factory.Make(md5Flag);
                    using var inpFile = File.OpenRead(info.Name);
                    //Log.Ok($"{info.Name} size {info.File.Length}b");
                    while (sentSizeThe < info.File.Length)
                    {
                        wantSize = InitBufferSize;
                        if ((sentSizeThe + wantSize) > info.File.Length)
                        {
                            wantSize = (int)(info.File.Length - sentSizeThe);
                        }
                        if (1 > wantSize) break;

                        readTask = Helper.Read(inpFile, buf2, wantSize,
                            cancellationTokenSource.Token);
                        readTask.Wait();
                        readRealSize = readTask.Result;
                        //Log.Ok($"{info.Name} read {readRealSize}b");

                        md5.Add(buf2, readRealSize);

                        sendTask = SendAndGetResponse();
                        sendTask.Wait();
                        //Log.Ok($"{info.Name} recv {sendTask.Result} < RSP (sentSize:{sentSizeThe}b)");
                    }

                    if (false == await byte02.As(0).Send(socketThe, cancellationTokenSource.Token))
                    {
                        Log.Error($"Fail to send end block!");
                        break;
                    }
                    if (false == await byte02.As(0).Send(socketThe, cancellationTokenSource.Token))
                    {
                        Log.Error($"Fail to send end data-size!");
                        break;
                    }

                    #region MD5
                    var hash = md5.Get();
                    wantSize = hash.Length;
                    if (wantSize > 0)
                    {
                        if (false == await byte02.As(wantSize).Send(socketThe, cancellationTokenSource.Token))
                        {
                            Log.Error($"Fail to send MD5 size!");
                            break;
                        }
                        if (wantSize != await Helper.Send(socketThe, hash, wantSize, cancellationTokenSource.Token))
                        {
                            Log.Error($"Fail to MD5!");
                            break;
                        }
                    }
                    #endregion
                }
                catch (Exception ee2)
                {
                    Log.Error($"'{info.Name}' {ee2}");
                }

                cntFile += 1;
                sumSize += info.File.Length;
                sumSent += sentSizeThe;
            }
            Log.Debug($"sentCntFile:{cntFile}");
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
