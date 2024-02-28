using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;
namespace rcopy2;

static class Client
{
    public static async Task<int> Run(string ipServer, IEnumerable<Info> infos)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var byte01 = new Byte1();
        var byte02 = new Byte2();
        var byte16 = new Byte16();
        Buffer buffer;

        bool statusTxfr;
        async Task<int> SendFileInfo(Socket socket, Info info)
        {
            var byte16 = new Byte16();

            long tmp2 = new DateTimeOffset(info.File.LastWriteTimeUtc).ToUnixTimeSeconds();
            Log.Debug($"Send fileTime: 0x{tmp2:x}");
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
            if ((false == statusTxfr) || (rsp16 != 0))
            {
                Log.Error($"Error info response (status:{statusTxfr};RSP:{rsp16})");
                return 0;
            }
            return cntTxfr + 32;
        }

        Socket socketThe;
        long sentSizeThe = 0;
        int wantSize = 0;
        byte bufferCode;
        int maxBufferSize;

        async Task<int> SendAndGetResponse(int sizeToBeSent)
        {
            if (sizeToBeSent == maxBufferSize)
            {
                if (false == await byte01.As(bufferCode).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send codeOfBuffer!");
                    return 0;
                }
                Log.Debug($"Send codeOfBuffer:0x{bufferCode:x} ok");
            }
            else
            {
                if (false == await byte01.As(0).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send last init code!");
                    return 0;
                }
                if (false == await byte02.As(sizeToBeSent).Send(socketThe, cancellationTokenSource.Token))
                {
                    Log.Error($"Fail to send last data-size {sizeToBeSent}b; 0x{sizeToBeSent:x}");
                    return 0;
                }
                Log.Debug($"Send last buffer size {sizeToBeSent}, 0x{sizeToBeSent:x} ok");
            }

            var sendTask = Helper.Send(socketThe, buffer.OutputData(), sizeToBeSent,
                cancellationTokenSource.Token);
            sendTask.Wait();
            int cntTxfr = sendTask.Result;
            Log.Debug($"Send buffer want:{sizeToBeSent}b; real:{cntTxfr}b");

            if (1 > cntTxfr) return 0;
            sentSizeThe += cntTxfr;
            (statusTxfr, var rsp16) = await byte16.Receive(socketThe,
                cancellationTokenSource.Token);
            Log.Debug($"Recv SendAndGetResponse: RSP msg (status:{statusTxfr}; RSP:{rsp16} (want:{sentSizeThe})");
            if ((false == statusTxfr) || (rsp16 != sentSizeThe))
            {
                Log.Error($"RSP to SendAndGetResponse: (status:{statusTxfr}; RSP:{rsp16} but want:{sentSizeThe})");
            }
            return cntTxfr;
        }

        int cntFile = 0;
        long sumSent = 0;
        var endPointThe = ParseIpEndpoint(ipServer);
        var connectTimeout = new CancellationTokenSource(millisecondsDelay: 3000);
        Log.Ok($"Connect {endPointThe.Address} at port {endPointThe.Port} ..");
        using var serverThe = new TcpClient();
        try
        {
            await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
            Log.Verbose("Connected");
            socketThe = serverThe.Client;

            (statusTxfr, bufferCode, var md5Code) = await byte02.ReceiveBytes(socketThe,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to recv control code!");
                return -1;
            }

            if (false == await byte02.As(bufferCode, md5Code).Send(socketThe,
                cancellationTokenSource.Token))
            {
                Log.Error($"Fail to reply control code!");
                return -1;
            }

            maxBufferSize = Helper.GetBufferSize(bufferCode);
            Log.Debug($"Buffer code:{bufferCode} -> size:{maxBufferSize}b");
            buffer = new Buffer(maxBufferSize);

            foreach (var info in infos)
            {
                Log.Verbose($"File> {info.Name}");

                if (0 == await SendFileInfo(socketThe, info))
                {
                    break;
                }

                Task<int> readTask;
                Task<int> sendTask;
                sentSizeThe = 0;
                try
                {
                    var md5 = Md5Factory.Make(md5Code == Helper.MD5REQUIRED);
                    using var inpFile = File.OpenRead(info.Name);

                    int readRealSize = 0;

                    sendTask = Helper.Send(socketThe, buffer.OutputData(),
                        wantSize:0, token:cancellationTokenSource.Token);

                    wantSize = maxBufferSize;
                    readTask = Helper.Read(inpFile, buffer.InputData(), wantSize, md5,
                            cancellationTokenSource.Token);

                    while (true)
                    {
                        Task.WaitAll(sendTask, readTask);
                        readRealSize = readTask.Result;
                        if (readRealSize == 0)
                        {
                            Log.Debug($"{info.Name} read is completed");
                            break;
                        }
                        Log.Debug($"{info.Name} read {readRealSize}b and send.RSP:{sendTask.Result}");

                        buffer.Switch();

                        sendTask = SendAndGetResponse(readRealSize);

                        wantSize = maxBufferSize;

                        readTask = Helper.Read(inpFile, buffer.InputData(), wantSize, md5,
                            cancellationTokenSource.Token);
                    }

                    if (false == await byte01.As(0xFF).Send(socketThe, cancellationTokenSource.Token))
                    {
                        Log.Error($"Fail to send end block size code!");
                        break;
                    }

                    #region MD5
                    var hash = md5.Get();
                    wantSize = hash.Length;
                    if (wantSize > 0)
                    {
                        Log.Debug($"{info.Name} sending MD5 (length:{wantSize}b)");
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
                sumSent += sentSizeThe;
            }
            Log.Debug($"sentCntFile:{cntFile}");
            serverThe.Client.Shutdown(SocketShutdown.Both);
            Task.Delay(20).Wait();
            serverThe.Client.Close();
            Log.Ok($"Connection is closed (cntFile={cntFile}; sumSent={sumSent})");
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
