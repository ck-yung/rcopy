using System.Net.Sockets;
using System.Text;
using static rcopy.Helper;
namespace rcopy;

static class Client
{
    public static async Task<int> Run(string ipServer, string path)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var byte01 = new Byte1();
        var byte02 = new Byte2();
        var byte16 = new Byte16();
        Buffer buffer;

        bool statusTxfr;
        async Task<int> SendFileInfo(Socket socket, string path)
        {
            var byte16 = new Byte16();

            var bytesPath = Encoding.UTF8.GetBytes(path);
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
                Log.Debug("Send codeOfBuffer:0x{0:x} ok", bufferCode);
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
                Log.Debug("Send last buffer size {0}, 0x{0} ok", sizeToBeSent);
            }

            var sendTask = Helper.Send(socketThe, buffer.OutputData(), sizeToBeSent,
                cancellationTokenSource.Token);
            sendTask.Wait();
            int cntTxfr = sendTask.Result;
            Log.Debug("Send buffer want:{0}b; real:{1}b", sizeToBeSent, cntTxfr);

            if (1 > cntTxfr) return 0;
            sentSizeThe += cntTxfr;
            (statusTxfr, var rsp16) = await byte16.Receive(socketThe,
                cancellationTokenSource.Token);
            Log.Debug("Recv SendAndGetResponse: RSP msg (status:{0}; RSP:{1} (want:{2})",
                statusTxfr, rsp16, sentSizeThe);
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
        Log.Ok("Connect {0} at port {1} ..", endPointThe.Address, endPointThe.Port);
        using var serverThe = new TcpClient();
        try
        {
            await serverThe.ConnectAsync(endPointThe, connectTimeout.Token);
            Log.Verbose("Connected");
            socketThe = serverThe.Client;

            (statusTxfr, bufferCode, var serverControlCode) = await byte02.ReceiveBytes(socketThe,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to recv control code!");
                return -1;
            }

            if (false == await byte02.As(bufferCode, serverControlCode).Send(socketThe,
                cancellationTokenSource.Token))
            {
                Log.Error($"Fail to reply control code!");
                return -1;
            }

            maxBufferSize = Helper.GetBufferSize(bufferCode);
            Log.Debug("Buffer code:{0} -> size:{1}b", bufferCode, maxBufferSize);
            buffer = new Buffer(maxBufferSize);

            //foreach (var info in new Info[] {arg})
            foreach (var filename in new string[] {path})
            {
                Log.Verbose("File> '{0}'", filename);

                if (0 == await SendFileInfo(socketThe, filename))
                {
                    break;
                }

                Task<int> readTask;
                Task<int> sendTask;
                sentSizeThe = 0;
                try
                {
                    var inp = new OpenFile(filename);
                    var inpFile = inp.Stream;

                    int readRealSize = 0;

                    sendTask = Helper.Send(socketThe, buffer.OutputData(),
                        wantSize:0, token:cancellationTokenSource.Token);

                    wantSize = maxBufferSize;
                    readTask = Helper.Read(inpFile, buffer.InputData(), wantSize,
                            cancellationTokenSource.Token);

                    while (true)
                    {
                        Task.WaitAll(sendTask, readTask);
                        readRealSize = readTask.Result;
                        if (readRealSize == 0)
                        {
                            Log.Debug("{0} read is completed", filename);
                            break;
                        }
                        Log.Debug("{0} read {1}b and send.RSP:{2}",
                            filename, readRealSize, sendTask.Result);

                        buffer.Switch();

                        sendTask = SendAndGetResponse(readRealSize);

                        wantSize = maxBufferSize;

                        readTask = Helper.Read(inpFile, buffer.InputData(), wantSize,
                            cancellationTokenSource.Token);
                    }

                    if (false == await byte01.As(0xFF).Send(socketThe, cancellationTokenSource.Token))
                    {
                        Log.Error($"Fail to send end block size code!");
                        break;
                    }
                    inp.Close();
                }
                catch (Exception ee2)
                {
                    Log.Error($"'{filename}' {ee2}");
                }

                cntFile += 1;
                sumSent += sentSizeThe;
            }
            //Log.Debug($"sentCntFile:{cntFile}");
            serverThe.Client.Shutdown(SocketShutdown.Both);
            Task.Delay(20).Wait();
            serverThe.Client.Close();
            Log.Ok("Connection is closed (sent={0}b)", sumSent);
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
