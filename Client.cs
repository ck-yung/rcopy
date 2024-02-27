using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;
namespace rcopy2;

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

    public byte[] ReadBuffer()
    {
        return flag ? bufferA : bufferB;
    }

    public byte[] SendBuffer()
    {
        return flag ? bufferB : bufferA;
    }

    public bool Switch()
    {
        flag = !flag;
        return flag;
    }
}

static class Client
{
    public static async Task<int> Run(string ipServer, IEnumerable<Info> infos)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        bool statusTxfr = false;
        var byte02 = new Byte2();
        var byte16 = new Byte16();
        var buffer = new Buffer(InitBufferSize);

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

            var bytesPath = Encoding.UTF8.GetBytes(info.Name);
            byte02.As(bytesPath.Length);
            if (false == await byte02.Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send name-size");
                return 0;
            }

            int cntTxfr = await Send(socket, bytesPath, bytesPath.Length,
                cancellationTokenSource.Token);
            if (cntTxfr != bytesPath.Length)
            {
                Log.Error($"Sent message error!, want={bytesPath.Length} but real={cntTxfr}");
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
        UInt16 codeOfBuffer = 0;
        async Task<int> SendAndGetResponse(int sizeToBeSent)
        {
            Log.Debug($"SendAndGetResponse(sizeToBeSent:{sizeToBeSent})");
            if (sizeToBeSent == InitBufferSize)
            {
                if (false == await byte02.As(codeOfBuffer).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send init code!");
                    return 0;
                }
                Log.Debug("Send codeOfBuffer ok");
            }
            else
            {
                if (false == await byte02.As(0).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send last init code!");
                    return 0;
                }
                if (false == await byte02.As(sizeToBeSent).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send last data-size {sizeToBeSent}b");
                    return 0;
                }
                Log.Debug($"Send last buffer size {sizeToBeSent} ok");
            }

            var sendTask = Helper.Send(socketThe, buffer.SendBuffer(), sizeToBeSent,
                cancellationTokenSource.Token);
            sendTask.Wait();
            int cntTxfr = sendTask.Result;
            Log.Debug($"Send buffer real:{cntTxfr}b; want:{sizeToBeSent}b [real:{cntTxfr}b]");

            if (1 > cntTxfr) return 0;
            sentSizeThe += cntTxfr;
            (statusTxfr, var rsp16) = await byte16.Receive(socketThe,
                cancellationTokenSource.Token);
            if ((false == statusTxfr) || (rsp16 != sentSizeThe))
            {
                Log.Error($"RSP to SendAndGetResponse: (status:{statusTxfr}; RSP:{rsp16} but want:{sentSizeThe})");
            }
            //else Log.Ok($"RSP ok {rsp16}");
            Log.Debug($"SendAndGetResponse(sizeToBeSent:{sizeToBeSent}) reply {cntTxfr}");
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
                Log.Debug($"Send info '{info.Name}' ok");

                Task<int> readTask;
                Task<int> sendTask;
                sentSizeThe = 0;
                try
                {
                    var md5 = Md5Factory.Make(md5Flag);
                    using var inpFile = File.OpenRead(info.Name);
                    //Log.Ok($"{info.Name} size {info.File.Length}b");

                    int readRealSize = 0;

                    sendTask = Helper.Send(socketThe, buffer.SendBuffer(),
                        wantSize:0, token:cancellationTokenSource.Token);

                    wantSize = InitBufferSize;
                    if (wantSize > info.File.Length)
                    {
                        wantSize = (int)(info.File.Length);
                    }
                    readTask = Helper.Read(inpFile, buffer.ReadBuffer(), wantSize, md5,
                            cancellationTokenSource.Token);

                    while (sentSizeThe < info.File.Length)
                    {
                        Task.WaitAll(sendTask, readTask);
                        readRealSize = readTask.Result;
                        if (readRealSize == 0)
                        {
                            break;
                        }
                        Log.Debug($"{info.Name} read {readRealSize}b and send.RSP:{sendTask.Result})");

                        buffer.Switch();

                        sendTask = SendAndGetResponse(readRealSize);

                        wantSize = InitBufferSize;
                        if ((sentSizeThe + wantSize) > info.File.Length)
                        {
                            wantSize = (int)(info.File.Length - sentSizeThe);
                        }
                        if (1 > wantSize) break;

                        readTask = Helper.Read(inpFile, buffer.ReadBuffer(), wantSize, md5,
                            cancellationTokenSource.Token);

                        // **** sendTask.Wait();
                        // **** readTask.Wait();
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
