using System.Net.Sockets;
using System.Text;
using static rcopy2.Helper;

namespace rcopy2;

static class Server
{
    public static bool Run(string ipServer, IEnumerable<string> args)
    {
        #region OUT-DIR
        (var outdir, var argsRest) = Options.Get("--out-dir",
            args.Select((it) => new FlagedArg(false, it)));

        Func<string, string> ToStandardDirSep =
            (it) => it.Replace('/', '\\');
        if (Path.DirectorySeparatorChar == '/')
        {
            ToStandardDirSep = (it) => it.Replace('\\', '/');
        }

        Func<string, string> ToOutputFilename =
            (it) => ToStandardDirSep(it);
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
        #endregion

        UInt16 ControlCode = CodeOfBuffer;
        #region --md5
        (var md5CtrlText, argsRest) = Options.Get("--md5", argsRest);
        var md5Flag = true;
        if (md5CtrlText == "off")
        {
            ControlCode += Md5Skipped;
            md5Flag = false;
        }
        else if (md5CtrlText == "on" || string.IsNullOrEmpty(md5CtrlText))
        {
            ControlCode += Md5Required;
        }
        else
        {
            throw new ArgumentException(
                $"Value to '--md5' should be 'on' or 'off' but not '{md5CtrlText}'");
        }
        #endregion

        #region --no-dir
        (var noDir, argsRest) = Options.Get("--no-dir", argsRest);
        if (false == string.IsNullOrEmpty(md5CtrlText))
        {
            if (noDir == "on")
            {
                ToOutputFilename = (path) => Path.GetFileName(path);
            }
            else if (noDir != "off")
            {
                throw new ArgumentException(
                    $"Value to '--no-dir' should be 'on' or 'off' but not '{noDir}'");
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

                            if (false == await byte02.As(ControlCode).Send(socketThe,
                                cancellationTokenSource.Token))
                            {
                                Log.Error($"Fail to send the code of buffer size");
                                return;
                            }

                            (statusTxfr, tmp02) = await byte02.Receive(clSocket,
                                cancellationTokenSource.Token);
                            if (ControlCode != tmp02)
                            {
                                Log.Ok($"#{idCnn} reply different CtrlCode x{tmp02:x4} from system x{ControlCode:x4}");
                                return;
                            }
                            var upperControlFlag = (tmp02 & 0xFF00);
                            var md5FlagThe = (upperControlFlag & Md5Required) == Md5Required;
                            if (md5FlagThe != md5Flag)
                            {
                                Log.Ok($"#{idCnn} reply different MD5-Flag");
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
                                var fileTime = DateTimeOffset.Now;
                                try
                                {
                                    fileTime = DateTimeOffset.FromUnixTimeSeconds(tmp16);
                                }
                                catch (Exception eeDt)
                                {
                                    Log.Error($"FromUnixTimeSeconds({tmp16}) failed! {eeDt}");
                                }

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

                                Log.Debug($"Read length-of-file-name:{sizeWant}");
                                cntTxfr = await Helper.Recv(socketThe, buf3,
                                    sizeWant, cancellationTokenSource.Token);
                                if (cntTxfr != sizeWant)
                                {
                                    Log.Ok($"Read file-name failed! (want:{sizeWant} but real:{cntTxfr})");
                                    break;
                                }
                                var fileName = Encoding.UTF8.GetString(buf3, 0, cntTxfr);
                                Log.Debug($"#{idCnn} > {fileSizeWant,10} {fileTime:s} '{fileName}'");
                                Log.Ok($"#{idCnn} > {fileName}");

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

                                int wantSize = 0;
                                fileSizeRecv = 0;
                                var md5 = Md5Factory.Make(md5FlagThe);
                                using (var outFs = File.Create(outputShadowFilename))
                                {
                                    while (fileSizeWant >= fileSizeRecv)
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
                                            wantSize = InitBufferSize;
                                            Log.Debug($"recv CodeOfBuffer");
                                        }
                                        else
                                        {
                                            (statusTxfr, tmp02) = await byte02.Receive(
                                                socketThe, cancellationTokenSource.Token);
                                            if (false == statusTxfr)
                                            {
                                                break;
                                            }
                                            wantSize = tmp02;
                                            Log.Debug($"recv wantSize:{tmp02}b");
                                        }

                                        if (wantSize == 0) break;

                                        cntTxfr = await Helper.Recv(socketThe, buf2, wantSize,
                                            cancellationTokenSource.Token);
                                        Log.Debug($"recv realSize:{cntTxfr}b");
                                        if (1 > cntTxfr) break;
                                        fileSizeRecv += cntTxfr;

                                        if (false == await byte16.As(fileSizeRecv).Send(socketThe,
                                            cancellationTokenSource.Token))
                                        {
                                            Log.Error($"Fail to send response (recvSize:{fileSizeRecv}");
                                            break;
                                        }

                                        md5.AddData(buf2, cntTxfr);

                                        outFs.Write(buf2, 0, cntTxfr);
                                    }
                                }

                                cntFille += 1;
                                sumSize += fileSizeRecv;
                                if (fileSizeWant!= fileSizeRecv)
                                {
                                    Log.Error($"#{idCnn} > fileSizeRecv:{fileSizeRecv}b but want:{fileSizeWant}b");
                                }

                                #region MD5
                                var hash = md5.Get();
                                if (hash.Length > 0)
                                {
                                    (statusTxfr, tmp02) = await byte02.Receive(socketThe,
                                        cancellationTokenSource.Token);
                                    if (false == statusTxfr)
                                    {
                                        break;
                                    }
                                    if (1 > tmp02)
                                    {
                                        Log.Error("MD5 size is ZERO!");
                                        break;
                                    }
                                    cntTxfr = await Helper.Recv(socketThe, buf2, tmp02,
                                        cancellationTokenSource.Token);
                                    if (cntTxfr > 0)
                                    {
                                        if (false == hash.Compare(buf2))
                                        {
                                            var md5The = BitConverter
                                            .ToString(hash, startIndex: 0, length: hash.Length)
                                            .Replace("-", "").ToLower();
                                            var md5Recv = BitConverter
                                            .ToString(buf2, startIndex: 0,
                                            length: int.Min(hash.Length, cntTxfr))
                                            .Replace("-", "").ToLower();
                                            Log.Error($"MD5 is mis-matched! Data:{md5The} but Recv:{md5Recv}");
                                        }
                                    }
                                }
                                #endregion

                                #region Rename outputShadowFilename to outputRealFilename
                                if (File.Exists(outputShadowFilename))
                                {
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
                                            File.SetLastWriteTime(path: outputRealFilename,
                                                lastWriteTime: fileTime.DateTime);
                                        }
                                        catch (Exception)
                                        {
                                            Log.Error(
                                                $"Fail to rename '{outputShadowFilename}' as '{outputRealFilename}'");
                                        }
                                    }
                                }
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
