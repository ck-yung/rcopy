using System.Collections.Immutable;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Server
{
    public static bool Run(string ipServer, IEnumerable<FlagedArg> argsRest)
    {
        #region --keep-dir
        (var keepDir, argsRest) = Options.Parse("--keep-dir", argsRest);

        Func<string, string> MakeStandardDirSepFunc()
        {
            if (Path.DirectorySeparatorChar == '/')
            {
                return  (it) => it.Replace('\\', '/');
            }
            return (it) => it.Replace('/', '\\');
        }

        Func<string, string> ToStandardDirSep;
        switch (keepDir)
        {
            case "":
                ToStandardDirSep = MakeStandardDirSepFunc();
                break;
            case "on":
                ToStandardDirSep = MakeStandardDirSepFunc();
                break;
            case "off":
                ToStandardDirSep = (it) => Path.GetFileName(it);
                break;
            default:
                throw new ArgumentException(
                    $"Value to '--keep-dir' should be 'on' or 'off' but not '{keepDir}'");
        }
        #endregion

        #region OUT-DIR
        (var outdir, argsRest) = Options.Parse("--out-dir", argsRest);

        Func<string, string> ToOutputFilename;
        if (false == string.IsNullOrEmpty(outdir))
        {
            if (false == Directory.Exists(outdir))
            {
                throw new ArgumentException(
                    $"Output dir '{outdir}' is NOT found!");
            }
            Log.Ok($"Output dir is '{outdir}'");
            ToOutputFilename = (it) => Path.Join(outdir,
                ToStandardDirSep(it));
        }
        else
        {
            ToOutputFilename = (it) => ToStandardDirSep(it);
        }
        #endregion

        byte codeOfBufferSize = Helper.DefaultCodeOfBufferSize;
        #region --buffer-size
        (var codeOfBufferText, argsRest) = Options.Parse("--buffer-size", argsRest);
        switch (codeOfBufferText)
        {
            case "":
                codeOfBufferSize = Helper.DefaultCodeOfBufferSize;
                break;
            case "1": // 8K
                codeOfBufferSize = 1;
                break;
            case "2": // 16K
                codeOfBufferSize = 2;
                break;
            case "3": // 32K
                codeOfBufferSize = 3;
                break;
            case "4": // 64K
                codeOfBufferSize = 4;
                break;
            default:
                throw new ArgumentException(
                    $"Value '{codeOfBufferText}' to '--buffer-size' is invalid.");
        }
        #endregion
        var maxBufferSize = Helper.GetBufferSize(codeOfBufferSize);
        Log.Verbose("BufferSize code:{0} -> {1}; 0x{1:x}",
            codeOfBufferSize, maxBufferSize);

        byte serverControlCode = INIT_CTRL_CODE;
        #region init control code
        // (var md5CtrlText, argsRest) = Options.Parse("--md5", argsRest);
        #endregion

        Func<string, string, bool> ipAllow;
        #region --allow
        (var ipMasks, argsRest) = Options.ParseForStrings("--allow", argsRest);
        if (ipMasks.Length == 0)
        {
            ipAllow = (_, _) => true;
        }
        else
        {
            var aa = ipMasks
                .Distinct()
                .Select((it) => MakeIpMask(it, "--allow"))
                .ToArray();
            if (aa.Length == 0)
            {
                ipAllow = (_, _) => true;
            }
            else
            {
                ipAllow = (a2, a3) => aa
                    .Any((it) => it(a2, a3));
            }
        }
        #endregion

        var cancellationTokenSource = new CancellationTokenSource();

        var endPointThe = ParseIpEndpoint(ipServer);
        var listener = new TcpListener(endPointThe);
        Log.Ok("Start listen on {0} at {1}", endPointThe.Address, endPointThe.Port);
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

        Log.Ok("** ** Press Ctrl-C to break."); // TODO

        Task.Run(async () =>
        {
            bool isRunning = true;
            try
            {
                var socketQueue = new ClientQueue();
                while (isRunning)
                {
                    var remoteSocket = await listener.AcceptSocketAsync(
                        cancellationTokenSource.Token);
                    socketQueue.Add(remoteSocket);

                    _ = Task.Run(async () =>
                    {
                        (var idCnn, var socketThe) = socketQueue.Get();
                        if (socketThe == null)
                        {
                            Log.Error("Error! No client connection is found!");
                            return;
                        }

                        var localEndPoint = socketThe.LocalEndPoint?.ToString() ?? "?1";
                        var remoteEndPoint = socketThe.RemoteEndPoint?.ToString() ?? "?2";
                        if (false == ipAllow(localEndPoint, remoteEndPoint))
                        {
                            Log.Ok("#{0} '{1}' is rejected to local:'{2}'",
                                idCnn, remoteEndPoint, localEndPoint);
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
                            return;
                        }
                        Log.Ok("#{0} '{1}' connected", idCnn, remoteEndPoint);

                        int cntFile = 0;
                        long sumSize = 0;
                        byte tmp01 = 0;
                        UInt16 tmp02 = 0;
                        var byte01 = new Byte1();
                        var byte02 = new Byte2();
                        var byte16 = new Byte16();
                        Buffer buffer = new(maxBufferSize);
                        try
                        {
                            bool statusTxfr = false;
                            int cntTxfr = 0;
                            UInt16 sizeWant = 0;
                            long fileSizeRecv = 0;

                            async Task<int> ReceiveAndSendResponse()
                            {
                                int wantSize = 0;
                                (statusTxfr, tmp01) = await byte01.Receive(
                                    socketThe, cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    Log.Error($"Read code-of-buffer failed!");
                                    return -1;
                                }

                                if (tmp01 == codeOfBufferSize)
                                {
                                    wantSize = maxBufferSize;
                                    Log.Debug("recv CodeOfBuffer");
                                }
                                else if (tmp01 == 0xFF)
                                {
                                    Log.Debug("recv EndOfData code");
                                    return 0;
                                }
                                else
                                {
                                    Log.Debug("recv code-of-buffer:0x{0:x2}", tmp01);
                                    (statusTxfr, tmp02) = await byte02.Receive(
                                        socketThe, cancellationTokenSource.Token);
                                    if (false == statusTxfr)
                                    {
                                        return -1;
                                    }
                                    if (tmp02 > maxBufferSize)
                                    {
                                        Log.Error($"recv WRONG wantSize:{tmp02}b; 0x{tmp02:x}");
                                        return -1;
                                    }
                                    wantSize = tmp02;
                                    Log.Debug("{0} recv wantSize:{1}b",
                                        nameof(ReceiveAndSendResponse), tmp02);
                                }

                                if (wantSize < 1)
                                {
                                    Log.Debug("{0} return ZERO", nameof(ReceiveAndSendResponse));
                                    return 0;
                                }

                                cntTxfr = await Helper.Recv(socketThe, buffer.InputData(), wantSize,
                                    cancellationTokenSource.Token);
                                Log.Debug("recv realSize:{0}b", cntTxfr);
                                if (1 > cntTxfr) return 0;

                                fileSizeRecv += cntTxfr;

                                if (false == await byte16.As(fileSizeRecv).Send(socketThe,
                                    cancellationTokenSource.Token))
                                {
                                    Log.Error($"Fail to send response (recvSize:{fileSizeRecv}");
                                }
                                return cntTxfr;
                            }

                            var recvBytes = new byte[2048];

                            if (false == await byte02.As(codeOfBufferSize, serverControlCode).Send(socketThe,
                                cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send the code of buffer size");
                                return;
                            }

                            (statusTxfr, var codeRecv, var recvControlCode) = await byte02.ReceiveBytes(socketThe,
                                cancellationTokenSource.Token);
                            if (codeOfBufferSize != codeRecv)
                            {
                                Log.Error($"#{idCnn} reply different CtrlCode x{tmp02:x4} from system x{codeOfBufferSize:x4}");
                                return;
                            }

                            if (recvControlCode != serverControlCode)
                            {
                                Log.Ok("#{0} reply different Control-Code", idCnn);
                            }

                            while (isRunning)
                            {
                                (statusTxfr, sizeWant) = await byte02.Receive(
                                    socketThe, cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    break;
                                }
                                if (sizeWant == 0)
                                {
                                    Log.Ok("Read length-of-file-name failed!");
                                    break;
                                }

                                Log.Debug("Read length-of-file-name:{0}", sizeWant);
                                cntTxfr = await Helper.Recv(socketThe, recvBytes,
                                    sizeWant, cancellationTokenSource.Token);
                                if (cntTxfr != sizeWant)
                                {
                                    Log.Error($"Read file-name failed! (want:{sizeWant} but real:{cntTxfr})");
                                    break;
                                }
                                var fileName = Encoding.UTF8.GetString(recvBytes, 0, cntTxfr);
                                Log.Verbose("#{0} > '{1}'", idCnn, fileName);

                                if (false == await byte16.As(0).Send(socketThe,
                                    cancellationTokenSource.Token))
                                {
                                    Log.Error($"Fail to send 1st response");
                                    break;
                                }

                                var outputRealFilename = ToOutputFilename(fileName);
                                Log.Debug("Real output file = '{0}'", outputRealFilename);

                                var prefixShadowFilename = "rcopy2_"
                                + Path.GetFileName(fileName)
                                + DateTime.Now.ToString("_yyyy-MMdd_HHmm-ffff");

                                var outputShadowFilename = ToOutputFilename(prefixShadowFilename + ".tmp");
                                int tryCnt = 0;
                                while (File.Exists(outputShadowFilename))
                                {
                                    tryCnt += 1;
                                    outputShadowFilename = ToOutputFilename(
                                        prefixShadowFilename + $".{tryCnt}.tmp");
                                }
                                Log.Debug("shadow file = '{0}'", outputShadowFilename);

                                fileSizeRecv = 0;
                                int writeWantSize;
                                int writeRealResult;
                                int seqThe = 0;
                                using (var outFs = File.Create(outputShadowFilename))
                                {
                                    Task<int> writeTask = Helper.Write(outFs, buffer.OutputData(),
                                        wantSize:0, cancellationTokenSource.Token);
                                    Task<int> recvTask = ReceiveAndSendResponse();
                                    while (true) // (fileSizeWant >= fileSizeRecv)
                                    {
                                        Task.WaitAll(writeTask, recvTask);
                                        writeRealResult = writeTask.Result;
                                        writeWantSize = recvTask.Result;
                                        Log.Debug("seq:{0} writeDone:{1}; writeWant:{2}",
                                            seqThe, writeRealResult, writeWantSize);
                                        if (1 > writeWantSize)
                                        {
                                            Log.Debug("Recv '{0}' is completed", outputRealFilename);
                                            break;
                                        }
                                        seqThe++;
                                        buffer.Switch();
                                        writeTask = Helper.Write(outFs, buffer.OutputData(),
                                            wantSize: writeWantSize, cancellationTokenSource.Token);
                                        recvTask = ReceiveAndSendResponse();
                                    }
                                }

                                cntFile += 1;
                                sumSize += fileSizeRecv;
                                //if (0 != fileSizeWant && fileSizeWant!= fileSizeRecv)
                                //{
                                //    Log.Error($"#{idCnn} > fileSizeRecv:{fileSizeRecv}b but want:{fileSizeWant}b");
                                //}

                                #region Rename outputShadowFilename to outputRealFilename
                                await Task.Run(async () =>
                                {
                                    if (File.Exists(outputShadowFilename))
                                    {
                                        await Task.Delay(10);
                                        if (File.Exists(outputRealFilename))
                                        {
                                            Console.Error.WriteLine(
                                                $"'{outputRealFilename}' <- '{outputShadowFilename}' failed because already existed.");
                                        }
                                        else
                                        {
                                            try
                                            {
                                                var dirThe = Path.GetDirectoryName(outputRealFilename);
                                                if (false == string.IsNullOrEmpty(dirThe) &&
                                                false == Directory.Exists(dirThe))
                                                {
                                                    Directory.CreateDirectory(dirThe);
                                                }
                                                File.Move(outputShadowFilename, outputRealFilename);
                                                //if (0 != fileTime)
                                                //{
                                                //    var theTime = DateTimeOffset.FromUnixTimeSeconds(fileTime);
                                                //    Log.Debug($"FileTime'{theTime:s}';0x{fileTime:x} -> '{outputRealFilename}'");
                                                //    File.SetLastWriteTime(path: outputRealFilename,
                                                //        lastWriteTime: theTime.DateTime);
                                                //    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                                //    || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                                                //    {
                                                //        File.SetCreationTime(path: outputRealFilename, creationTime: DateTime.Now);
                                                //    }
                                                //}
                                                //else Log.Verbose($"Skip ZERO-fileTime '{outputRealFilename}'");
                                            }
                                            catch (Exception eer)
                                            {
                                                Log.Error(
                                                    $"Fail to rename '{outputShadowFilename}' as '{outputRealFilename}' {eer.Message}");
                                            }
                                        }
                                    }
                                });
                                #endregion

                                await Task.Delay(1000);
                                isRunning = false;
                            }
                        }
                        catch (Exception ee)
                        {
                            Log.Error($"#{idCnn} '{remoteEndPoint}' {ee}");
                        }
                        finally
                        {
                            Log.Ok("#{0} '{1}' dropped (sent:{2}b)",
                                idCnn, remoteEndPoint, sumSize);
                            await Task.Delay(20);
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
                        }
                        await Task.Delay(1000);
                        if (false == isRunning)
                        {
                            listener.Stop();
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
                if (true == isRunning)
                {
                    Log.Error("Accept: " + ee.GetType().Name + ": " + ee.Message);
                }
            }

            try
            {
                await Task.Delay(20);
                listener.Stop();
                await Task.Delay(20);
            }
            catch (Exception ee)
            {
                if (true == isRunning)
                {
                    Log.Error(ee.GetType().Name + ": " + ee.Message);
                }
            }
        }).Wait();

        Task.Delay(20).Wait();
        return true;
    }
}
