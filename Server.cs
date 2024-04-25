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
            Console.WriteLine($"Output dir is '{outdir}'");
            ToOutputFilename = (it) => Path.Join(outdir,
                ToStandardDirSep(it));
        }
        else
        {
            ToOutputFilename = (it) => ToStandardDirSep(it);
        }
        #endregion

        byte codeOfBuffserSize = Helper.DefaultCodeOfBufferSize;
        #region --buffer-size
        (var codeOfBufferText, argsRest) = Options.Parse("--buffer-size", argsRest);
        switch (codeOfBufferText)
        {
            case "":
                codeOfBuffserSize = Helper.DefaultCodeOfBufferSize;
                break;
            case "1": // 8K
                codeOfBuffserSize = 1;
                break;
            case "2": // 16K
                codeOfBuffserSize = 2;
                break;
            case "3": // 32K
                codeOfBuffserSize = 3;
                break;
            case "4": // 64K
                codeOfBuffserSize = 4;
                break;
            default:
                throw new ArgumentException(
                    $"Value '{codeOfBufferText}' to '--buffer-size' is invalid.");
        }
        #endregion
        var maxBufferSize = Helper.GetBufferSize(codeOfBuffserSize);
        Log.Verbose($"BufferSize code:{codeOfBuffserSize} -> {maxBufferSize}; 0x{maxBufferSize:x}");

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
                while (true)
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

                        var remoteEndPoint = socketThe.RemoteEndPoint;
                        if (false == ipAllow(socketThe.LocalEndPoint?.ToString() ?? "?1",
                            remoteEndPoint?.ToString() ?? "?2"))
                        {
                            Log.Ok($"#{idCnn} '{remoteEndPoint}' is rejected to local:'{socketThe.LocalEndPoint}'");
                            socketThe.Shutdown(SocketShutdown.Both);
                            await Task.Delay(20);
                            socketThe.Close();
                            return;
                        }
                        Log.Ok($"#{idCnn} '{remoteEndPoint}' connected");

                        int cntFille = 0;
                        long sumSize = 0;
                        byte tmp01 = 0;
                        UInt16 tmp02 = 0;
                        long tmp16 = 0;
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

                                if (tmp01 == codeOfBuffserSize)
                                {
                                    wantSize = maxBufferSize;
                                    Log.Debug($"recv CodeOfBuffer");
                                }
                                else if (tmp01 == 0xFF)
                                {
                                    Log.Debug($"recv EndOfData code");
                                    return 0;
                                }
                                else
                                {
                                    Log.Debug($"recv code-of-buffer:0x{tmp01:x}");
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
                                    Log.Debug($"{nameof(ReceiveAndSendResponse)} recv wantSize:{tmp02}b");
                                }

                                if (wantSize < 1)
                                {
                                    Log.Debug($"{nameof(ReceiveAndSendResponse)} return ZERO");
                                    return 0;
                                }

                                cntTxfr = await Helper.Recv(socketThe, buffer.InputData(), wantSize,
                                    cancellationTokenSource.Token);
                                Log.Debug($"recv realSize:{cntTxfr}b");
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

                            if (false == await byte02.As(codeOfBuffserSize, serverControlCode).Send(socketThe,
                                cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send the code of buffer size");
                                return;
                            }

                            (statusTxfr, var codeRecv, var recvControlCode) = await byte02.ReceiveBytes(socketThe,
                                cancellationTokenSource.Token);
                            if (codeOfBuffserSize != codeRecv)
                            {
                                Log.Error($"#{idCnn} reply different CtrlCode x{tmp02:x4} from system x{codeOfBuffserSize:x4}");
                                return;
                            }

                            if (recvControlCode != serverControlCode)
                            {
                                Log.Ok($"#{idCnn} reply different Control-Code");
                            }

                            long fileSizeWant;
                            long fileTime;
                            while (true)
                            {
                                (statusTxfr, fileTime) = await byte16.Receive(socketThe,
                                    cancellationTokenSource.Token);
                                if (false == statusTxfr)
                                {
                                    break;
                                }
                                Log.Debug($"Recv fileTime: 0x{fileTime:x}");

                                (statusTxfr, tmp16) = await byte16.Receive(socketThe,
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

                                Log.Debug($"Read length-of-file-name:{sizeWant}");
                                cntTxfr = await Helper.Recv(socketThe, recvBytes,
                                    sizeWant, cancellationTokenSource.Token);
                                if (cntTxfr != sizeWant)
                                {
                                    Log.Error($"Read file-name failed! (want:{sizeWant} but real:{cntTxfr})");
                                    break;
                                }
                                var fileName = Encoding.UTF8.GetString(recvBytes, 0, cntTxfr);
                                Log.Debug($"#{idCnn} > {fileSizeWant,10} '{fileName}'");
                                Log.Verbose($"#{idCnn} > {fileName}");

                                if (false == await byte16.As(0).Send(socketThe,
                                    cancellationTokenSource.Token))
                                {
                                    Log.Error($"Fail to send 1st response");
                                    break;
                                }

                                var outputRealFilename = ToOutputFilename(fileName);
                                Log.Debug($"Real output file = '{outputRealFilename}'");

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
                                Log.Debug($"shadow file = '{outputShadowFilename}'");

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
                                        Log.Debug($"seq:{seqThe} writeDone:{writeRealResult}; writeWant:{writeWantSize}");
                                        if (1 > writeWantSize)
                                        {
                                            Log.Debug($"Recv '{outputRealFilename}' is completed");
                                            break;
                                        }
                                        seqThe++;
                                        buffer.Switch();
                                        writeTask = Helper.Write(outFs, buffer.OutputData(),
                                            wantSize: writeWantSize, cancellationTokenSource.Token);
                                        recvTask = ReceiveAndSendResponse();
                                    }
                                }

                                cntFille += 1;
                                sumSize += fileSizeRecv;
                                if (0 != fileSizeWant && fileSizeWant!= fileSizeRecv)
                                {
                                    Log.Error($"#{idCnn} > fileSizeRecv:{fileSizeRecv}b but want:{fileSizeWant}b");
                                }

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
                                                if (0 != fileTime)
                                                {
                                                    var theTime = DateTimeOffset.FromUnixTimeSeconds(fileTime);
                                                    Log.Debug($"FileTime'{theTime:s}';0x{fileTime:x} -> '{outputRealFilename}'");
                                                    File.SetLastWriteTime(path: outputRealFilename,
                                                        lastWriteTime: theTime.DateTime);
                                                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                                    || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                                                    {
                                                        File.SetCreationTime(path: outputRealFilename, creationTime: DateTime.Now);
                                                    }
                                                }
                                                else Log.Verbose($"Skip ZERO-fileTime '{outputRealFilename}'");
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
