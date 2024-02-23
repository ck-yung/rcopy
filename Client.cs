using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using static rcopy2.Helper;
namespace rcopy2;

static class Client
{
    public static async Task<int> Run(string ipServer, IEnumerable<Info> infos)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        UInt16 codeOfBuffer = 0;
        int cntTxfr = 0;
        bool statusTxfr = false;
        var byte02 = new Byte2();
        var byte16 = new Byte16();
        long rsp16 = 0;
        var buf2 = new byte[InitBufferSize];

        async Task<bool> SendFileInfo(Socket socket, Info info)
        {
            var byte16 = new Byte16();

            long tmp2 = new DateTimeOffset(info.File.LastWriteTimeUtc).ToUnixTimeSeconds();
            if (false == await byte16.From(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send date-time");
                return false;
            }

            tmp2 = info.File.Length;
            if (false == await byte16.From(tmp2).Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send file-size");
                return false;
            }

            var buf2 = Encoding.UTF8.GetBytes(info.Name);
            byte02.From(buf2.Length);
            if (false == await byte02.Send(socket, cancellationTokenSource.Token))
            {
                Log.Error($"Fail to send name-size");
                return false;
            }

            cntTxfr = await Send(socket, buf2, buf2.Length,
                cancellationTokenSource.Token);
            if (cntTxfr != buf2.Length)
            {
                Log.Error($"Sent message error!, want={buf2.Length} but real={cntTxfr}");
                return false;
            }

            (statusTxfr, rsp16) = await byte16.Receive(socket,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to read response");
                return false;
            }
            if (0!=rsp16)
            {
                Log.Ok($"RSP < {rsp16}");
            }
            return (rsp16 == 0);
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
            var socketThe = serverThe.Client;

            (statusTxfr, codeOfBuffer) = await byte02.Receive(socketThe,
                cancellationTokenSource.Token);
            if (false == statusTxfr)
            {
                Log.Error($"Fail to read buffer size");
                return -1;
            }
            Log.Ok($"Code of buffer size is {codeOfBuffer}");

            foreach (var info in infos)
            {
                if (info.File.Length < 1)
                {
                    Log.Ok($"Skip empty file '{info.Name}'");
                    continue;
                }
                if (false == await SendFileInfo(socketThe, info))
                {
                    break;
                }

                long sentSize = 0;
                int wantSentSize = 0;
                try
                {
                    var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                    using var inpFile = File.OpenRead(info.Name);
                    while (sentSize < info.File.Length)
                    {
                        wantSentSize = InitBufferSize;
                        if ((sentSize + wantSentSize) > info.File.Length)
                        {
                            wantSentSize = (int)(info.File.Length - sentSize);
                        }
                        if (1 > wantSentSize) break;

                        int readFileSize = inpFile.Read(buf2, 0, wantSentSize);
                        if (readFileSize != wantSentSize)
                        {
                            Log.Error(
                                $"Fail to read file '{info.Name}' at offset {sentSize} (want:{wantSentSize}b but real:{readFileSize}b)");
                        }

                        if (wantSentSize == InitBufferSize)
                        {
                            if (false == await byte02.From(codeOfBuffer).Send(socketThe, cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send init code!");
                                break;
                            }
                        }
                        else
                        {
                            if (false == await byte02.From(0).Send(socketThe, cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send last init code!");
                                break;
                            }
                            if (false == await byte02.From(wantSentSize).Send(socketThe, cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send last data-size {wantSentSize}b");
                                break;
                            }
                        }

                        cntTxfr = await Helper.Send(socketThe, buf2, wantSentSize,
                            cancellationTokenSource.Token);

                        md5.AppendData(buf2, 0, readFileSize);

                        //Log.Ok($"dbg: Sent {cntTxfr}b");
                        if (1 > cntTxfr) break;
                        sentSize += cntTxfr;
                        (statusTxfr, rsp16) = await byte16.Receive(socketThe,
                            cancellationTokenSource.Token);
                        if (false == statusTxfr)
                        {
                            Log.Error($"Fail to read response");
                            break;
                        }
                        if (rsp16 != sentSize)
                        {
                            Log.Ok($"Recv unknown rsp {rsp16} at sentSize:{sentSize} !");
                        }
                    }

                    if (false == await byte02.From(0).Send(socketThe, cancellationTokenSource.Token))
                    {
                        Log.Error($"Fail to send end block!");
                        break;
                    }
                    if (false == await byte02.From(0).Send(socketThe, cancellationTokenSource.Token))
                    {
                        Log.Error($"Fail to send end data-size!");
                        break;
                    }

                    var hash = md5.GetCurrentHash();
                    var md5Result = BitConverter.ToString(hash)
                        .Replace("-", "")
                        .ToLower();
                    wantSentSize = hash.Length;
                    if (wantSentSize > 0)
                    {
                        if (false == await byte02.From(wantSentSize).Send(socketThe, cancellationTokenSource.Token))
                        {
                            Log.Error($"Fail to send MD5 size!");
                            break;
                        }
                        if (wantSentSize != await Helper.Send(socketThe, hash, wantSentSize, cancellationTokenSource.Token))
                        {
                            Log.Error($"Fail to MD5!");
                            break;
                        }
                        Log.Ok($"MD5 '{md5Result}' <- '{info.Name}'");
                    }
                }
                catch (Exception ee2)
                {
                    Log.Error($"'{info.Name}' {ee2}");
                }

                cntFile += 1;
                sumSize += info.File.Length;
                sumSent += sentSize;
            }
            Log.Ok($"sentCntFile:{cntFile}");
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
