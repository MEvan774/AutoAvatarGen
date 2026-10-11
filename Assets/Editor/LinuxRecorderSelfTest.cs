#if UNITY_EDITOR_LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Evereal.VideoCapture;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Credit-free check of the Linux recorder backend — LinuxFFmpegNative, the
/// managed stand-in for Evereal's native FFmpegEncoder plugin. It drives the
/// same entry points FFmpegEncoder and FFmpegMuxer call, then inspects the
/// results with ffprobe:
///   1. ~3 s of 1080p frames in Unity's readback layout (RGBA, rows bottom-up),
///      red in the TOP half only, through Start/Capture/Stop — every other
///      frame sent with count 2, the way Evereal repeats frames to hold the
///      rate — then a 3 s sine wav muxed in like FFmpegMuxer does. ffprobe must
///      see h264 + aac at ~3 s tagged BT.709, and a decoded frame must be red on
///      top and black below: that catches a channel swap AND an upside-down
///      image. Eight colour bars must also survive the encode within a few
///      levels. All of it runs for each H.264 encoder: auto (NVENC where the
///      GPU allows), libx264 forced, and auto with an ffmpeg whose NVENC cannot
///      open — which has to fall back to libx264 by itself.
///   2. The real GPU readback — a camera rendering into the capture target
///      exactly as FFmpegEncoder creates it (ARGB32, 8x MSAA), read back by
///      AsyncGPUReadback and by ReadPixels — has the layout step 1 assumed,
///      and survives the same encode.
///   3. Failure contracts: an ffmpeg that dies mid-take makes CaptureVodFrames
///      return false (and the editor survives the SIGPIPE); a failed mux
///      returns IntPtr.Zero and leaves no partial file for FFmpegMuxer to
///      mistake for success.
///
///   Menu:  MugsTech ▸ Recording ▸ Run Linux Encoder Self-Test
///   Batch: Unity -batchmode -quit -projectPath &lt;project&gt; -logFile &lt;log&gt;
///            -executeMethod LinuxRecorderSelfTest.RunFromCommandLine [-selfTestOut &lt;dir&gt;]
/// Exit code 0 when every check passes. Output files stay in the out folder.
/// </summary>
public static class LinuxRecorderSelfTest
{
    const int Width = 1920, Height = 1080, Fps = 30, Kbps = 8000;
    const string OutDirArg = "-selfTestOut";
    const int TestLayer = 31;

    [MenuItem("MugsTech/Recording/Run Linux Encoder Self-Test")]
    static void RunFromMenu() => Run(DefaultOutDir());

    public static void RunFromCommandLine()
    {
        string dir = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == OutDirArg) dir = args[i + 1];
        bool ok = Run(string.IsNullOrWhiteSpace(dir) ? DefaultOutDir() : Path.GetFullPath(dir));
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static string DefaultOutDir() => Path.Combine(Path.GetTempPath(), "AutoAvatarGen_LinuxEncoderSelfTest");

    static bool Run(string outDir)
    {
        var log = new Log();
        Directory.CreateDirectory(outDir);
        Debug.Log($"[LinuxRecorderSelfTest] Output folder: {outDir}");

        string ffmpeg = FFmpegConfig.path;
        log.IsTrue(FFmpegConfig.IsExist(), $"FFmpegConfig.path resolves to an existing ffmpeg (got '{ffmpeg}')");
        log.IsTrue(ffmpeg != Path.GetFullPath(FFmpegConfig.macOSPath), "FFmpegConfig.path is not the bundled macOS binary");
        string ffprobe = string.IsNullOrEmpty(ffmpeg) ? "" : Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe");
        log.IsTrue(File.Exists(ffprobe), $"ffprobe next to ffmpeg ('{ffprobe}')");
        if (!FFmpegConfig.IsExist() || !File.Exists(ffprobe))
            return log.Report();

        try
        {
            LinuxFFmpegNative.H264Encoder = null;
            string auto = EncoderPass(log, outDir, ffmpeg, ffprobe, "auto");
            Debug.Log($"[LinuxRecorderSelfTest] auto picked {auto}.");
            if (auto != "h264_nvenc")
                Debug.LogWarning("[LinuxRecorderSelfTest] NVENC is not available here — only libx264 was exercised.");

            LinuxFFmpegNative.H264Encoder = "libx264";
            log.AreEqual("libx264", EncoderPass(log, outDir, ffmpeg, ffprobe, "libx264"), "forced libx264 is used");

            LinuxFFmpegNative.H264Encoder = null;
            log.AreEqual("libx264", EncoderPass(log, outDir, NvencLessFfmpeg(outDir, ffmpeg), ffprobe, "no-nvenc"),
                "auto falls back to libx264 when NVENC cannot open");

            GpuReadback(log, outDir, ffmpeg);
            DyingEncoder(log, outDir);
            FailedMux(log, outDir, ffmpeg);
        }
        catch (Exception e)
        {
            log.Fail("threw " + e);
        }
        finally
        {
            LinuxFFmpegNative.H264Encoder = null;
        }
        return log.Report();
    }

    // Runs the take and the colour check with the current encoder setting;
    // returns the H.264 encoder the shim actually used.
    static string EncoderPass(Log log, string dir, string ffmpeg, string ffprobe, string label)
    {
        SyntheticTake(log, dir, ffmpeg, ffprobe, label);
        string used = LinuxFFmpegNative.LastH264Encoder;
        ColourBars(log, dir, ffmpeg, label);
        return used;
    }

    // An ffmpeg wrapper that refuses anything mentioning h264_nvenc — the
    // machine-without-a-working-NVENC case — and passes everything else through.
    static string NvencLessFfmpeg(string dir, string ffmpeg)
    {
        string wrapper = Path.Combine(dir, "ffmpeg_without_nvenc.sh");
        File.WriteAllText(wrapper,
            "#!/bin/sh\n" +
            "case \"$*\" in *h264_nvenc*) echo 'selftest: NVENC unavailable' >&2; exit 1;; esac\n" +
            $"exec \"{ffmpeg}\" \"$@\"\n");
        RunProcess("chmod", $"+x \"{wrapper}\"", out _, out _);
        return wrapper;
    }

    // -----------------------------------------------------------------------
    // 1. Synthetic frames -> encode -> mux -> ffprobe
    // -----------------------------------------------------------------------

    static void SyntheticTake(Log log, string dir, string ffmpeg, string ffprobe, string label)
    {
        string video = Path.Combine(dir, $"synthetic_{label}_video.mp4");
        string wav   = Path.Combine(dir, $"synthetic_{label}_audio.wav");
        string media = Path.Combine(dir, $"synthetic_{label}_muxed.mp4");
        DeleteIfExists(video, wav, media);

        // Unity's layout: row 0 is the BOTTOM of the image.
        byte[] frame = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            bool top = y >= Height / 2;
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 4;
                frame[i] = (byte)(top ? 255 : 0);
                frame[i + 3] = 255;
            }
        }

        // FFmpegEncoder passes verticalFlip = true for camera captures.
        IntPtr api = LinuxFFmpegNative.StartVodCapture(EncoderPreset.H264_MP4, Width, Height, Kbps, Fps,
            true, false, ProjectionType.NONE, StereoMode.NONE, video, ffmpeg);
        log.IsTrue(api != IntPtr.Zero, $"{label}: StartVodCapture returns a handle");
        if (api == IntPtr.Zero) return;

        // 30 single + 30 doubled = 90 frames = 3.0 s.
        var clock = Stopwatch.StartNew();
        bool accepted = true;
        for (int i = 0; i < 60; i++)
            accepted &= LinuxFFmpegNative.CaptureVodFrames(api, frame, i % 2 == 0 ? 1 : 2);
        bool stopped = LinuxFFmpegNative.StopVodCapture(api);
        clock.Stop();
        LinuxFFmpegNative.CleanVodCapture(api);
        log.IsTrue(accepted, $"{label}: CaptureVodFrames accepts every frame");
        log.IsTrue(stopped, $"{label}: StopVodCapture reports ffmpeg exit 0");
        log.IsTrue(File.Exists(video), $"{label}: encoder wrote the video file");
        Debug.Log($"[LinuxRecorderSelfTest] {label} ({LinuxFFmpegNative.LastH264Encoder}): 90 synthetic {Width}x{Height} frames encoded in " +
                  $"{clock.Elapsed.TotalSeconds:F2}s ({90 / clock.Elapsed.TotalSeconds:F0} fps on flat content).");

        WriteSineWav(wav, 3.0, 48000, 2, 440.0);
        IntPtr mux = LinuxFFmpegNative.StartMuxingProcess(EncoderPreset.H264_MP4, Kbps, false, false,
            media, video, wav, ffmpeg, false);
        log.IsTrue(mux != IntPtr.Zero, $"{label}: StartMuxingProcess returns a handle");
        LinuxFFmpegNative.CleanMuxingProcess(mux);
        log.IsTrue(File.Exists(media), $"{label}: muxer wrote the media file");
        if (!File.Exists(media)) return;

        ProbeResult probe = Probe(ffprobe, media);
        log.AreEqual("h264", probe.Codec("video"), $"{label}: video codec");
        log.AreEqual("aac", probe.Codec("audio"), $"{label}: audio codec");
        log.AreEqual($"{Width}x{Height}", probe.VideoSize(), $"{label}: video size");
        log.Near(3.0f, probe.Duration(), 0.1f, $"{label}: muxed duration (s)");
        ProbeStream v = probe.Video();
        log.AreEqual("bt709", v?.color_space, $"{label}: colour matrix tag");
        log.AreEqual("bt709", v?.color_primaries, $"{label}: colour primaries tag");
        log.AreEqual("bt709", v?.color_transfer, $"{label}: colour transfer tag");
        log.AreEqual("tv", v?.color_range, $"{label}: colour range tag");

        CheckRedTopBlackBottom(log, DecodeFrameRgb24(ffmpeg, media, 1.5, Width, Height), $"{label} take");
    }

    // Eight vertical colour bars, encoded and decoded: each must come back
    // within a few levels — a wrong YUV matrix or range is off by 10-40.
    static readonly Color32[] Bars =
    {
        new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255),
        new Color32(128, 128, 128, 255), new Color32(224, 172, 140, 255), new Color32(30, 40, 90, 255),
        new Color32(255, 255, 255, 255), new Color32(16, 16, 16, 255),
    };

    static void ColourBars(Log log, string dir, string ffmpeg, string label)
    {
        string video = Path.Combine(dir, $"colour_bars_{label}.mp4");
        DeleteIfExists(video);
        byte[] frame = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Color32 c = Bars[x * Bars.Length / Width];
                int i = (y * Width + x) * 4;
                frame[i] = c.r; frame[i + 1] = c.g; frame[i + 2] = c.b; frame[i + 3] = 255;
            }

        IntPtr api = LinuxFFmpegNative.StartVodCapture(EncoderPreset.H264_MP4, Width, Height, Kbps, Fps,
            true, false, ProjectionType.NONE, StereoMode.NONE, video, ffmpeg);
        if (api == IntPtr.Zero) { log.Fail($"{label}: colour bars encode did not start"); return; }
        bool ok = LinuxFFmpegNative.CaptureVodFrames(api, frame, 10) & LinuxFFmpegNative.StopVodCapture(api);
        LinuxFFmpegNative.CleanVodCapture(api);
        log.IsTrue(ok, $"{label}: colour bars encode");
        if (!File.Exists(video)) return;

        byte[] rgb = DecodeFrameRgb24(ffmpeg, video, 0.1, Width, Height);
        if (rgb.Length != Width * Height * 3) { log.Fail($"{label}: colour bars decode"); return; }
        int worst = 0;
        var got = new StringBuilder();
        for (int b = 0; b < Bars.Length; b++)
        {
            Color32 c = AverageRgb(rgb, (int)((b + 0.5f) * Width / Bars.Length), Height / 2);
            int err = Mathf.Max(Mathf.Abs(c.r - Bars[b].r), Mathf.Max(Mathf.Abs(c.g - Bars[b].g), Mathf.Abs(c.b - Bars[b].b)));
            worst = Mathf.Max(worst, err);
            got.Append($" ({c.r},{c.g},{c.b})");
        }
        Debug.Log($"[LinuxRecorderSelfTest] {label}: colour bars decoded as{got} — worst channel error {worst}.");
        log.IsTrue(worst <= 4, $"{label}: colour bars within 4 levels — worst {worst}");
    }

    // -----------------------------------------------------------------------
    // 2. The real GPU readback layout
    // -----------------------------------------------------------------------

    static void GpuReadback(Log log, string dir, string ffmpeg)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            log.Fail("no graphics device (run without -nographics) — GPU readback layout NOT checked");
            return;
        }
        Debug.Log($"[LinuxRecorderSelfTest] GPU: {SystemInfo.graphicsDeviceType} on {SystemInfo.graphicsDeviceName}, " +
                  $"async readback {(SystemInfo.supportsAsyncGPUReadback ? "supported" : "NOT supported")}.");

        // The capture target exactly as FFmpegEncoder creates it, MSAA as
        // CrossPlatformRecorder.captureAntiAliasing sets it.
        RenderTexture rt = Utils.CreateRenderTexture(Width, Height, 24, 8, null);
        var camGo = new GameObject("LinuxRecorderSelfTest Camera") { hideFlags = HideFlags.HideAndDontSave };
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Material red = null;
        try
        {
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << TestLayer;
            cam.allowMSAA = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.targetTexture = rt;

            // A red quad over the top half of the view (y 0..1 of -1..1).
            quad.hideFlags = HideFlags.HideAndDontSave;
            quad.layer = TestLayer;
            quad.transform.position = new Vector3(0f, 0.5f, 0f);
            quad.transform.localScale = new Vector3(10f, 1f, 1f);
            red = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
            quad.GetComponent<MeshRenderer>().sharedMaterial = red;

            cam.Render();

            byte[] asyncBytes = null;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(rt);
                request.WaitForCompletion();
                log.IsTrue(!request.hasError, "AsyncGPUReadback of the 8x MSAA capture texture succeeds");
                if (!request.hasError)
                {
                    asyncBytes = request.GetData<byte>().ToArray();
                    CheckReadbackLayout(log, asyncBytes, "AsyncGPUReadback");
                }
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = Utils.CreateTexture(Width, Height, null);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            tex.Apply();
            RenderTexture.active = previous;
            byte[] readPixelsBytes = tex.GetRawTextureData();
            UnityEngine.Object.DestroyImmediate(tex);
            CheckReadbackLayout(log, readPixelsBytes, "ReadPixels");

            // The production path end to end: these exact bytes through the encoder.
            byte[] frame = asyncBytes ?? readPixelsBytes;
            string video = Path.Combine(dir, "gpu_readback_video.mp4");
            DeleteIfExists(video);
            IntPtr api = LinuxFFmpegNative.StartVodCapture(EncoderPreset.H264_MP4, Width, Height, Kbps, Fps,
                true, false, ProjectionType.NONE, StereoMode.NONE, video, ffmpeg);
            log.IsTrue(api != IntPtr.Zero, "StartVodCapture (GPU frames) returns a handle");
            if (api == IntPtr.Zero) return;
            bool accepted = LinuxFFmpegNative.CaptureVodFrames(api, frame, 15);
            bool stopped = LinuxFFmpegNative.StopVodCapture(api);
            LinuxFFmpegNative.CleanVodCapture(api);
            log.IsTrue(accepted && stopped, "GPU readback frames encode");
            if (File.Exists(video))
                CheckRedTopBlackBottom(log, DecodeFrameRgb24(ffmpeg, video, 0.2, Width, Height),
                    asyncBytes != null ? "AsyncGPUReadback take" : "ReadPixels take");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(quad);
            UnityEngine.Object.DestroyImmediate(camGo);
            if (red != null) UnityEngine.Object.DestroyImmediate(red);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    // Row 0 must be the bottom (black) and the last row the top (red), with R
    // as the first byte of each pixel.
    static void CheckReadbackLayout(Log log, byte[] data, string what)
    {
        log.AreEqual(Width * Height * 4, data.Length, $"{what}: byte count");
        if (data.Length != Width * Height * 4) return;
        int mid = Width / 2 * 4;
        int firstRow = mid;
        int lastRow = (Height - 1) * Width * 4 + mid;
        string first = $"({data[firstRow]},{data[firstRow + 1]},{data[firstRow + 2]},{data[firstRow + 3]})";
        string last = $"({data[lastRow]},{data[lastRow + 1]},{data[lastRow + 2]},{data[lastRow + 3]})";
        Debug.Log($"[LinuxRecorderSelfTest] {what}: row 0 = {first}, last row = {last}.");
        log.IsTrue(data[firstRow] < 10 && data[firstRow + 1] < 10 && data[firstRow + 2] < 10,
            $"{what}: row 0 is the image BOTTOM (black) — got {first}");
        log.IsTrue(data[lastRow] > 245 && data[lastRow + 1] < 10 && data[lastRow + 2] < 10,
            $"{what}: last row is the TOP (red), bytes in RGBA order — got {last}");
    }

    // -----------------------------------------------------------------------
    // 3. Failure contracts
    // -----------------------------------------------------------------------

    static void DyingEncoder(Log log, string dir)
    {
        // Never reads its stdin, exits 3 after half a second.
        string fake = Path.Combine(dir, "dying_ffmpeg.sh");
        File.WriteAllText(fake, "#!/bin/sh\nsleep 0.5\nexit 3\n");
        RunProcess("chmod", $"+x \"{fake}\"", out _, out _);

        IntPtr api = LinuxFFmpegNative.StartVodCapture(EncoderPreset.H264_MP4, 320, 240, 1000, Fps,
            true, false, ProjectionType.NONE, StereoMode.NONE, Path.Combine(dir, "never_written.mp4"), fake);
        log.IsTrue(api != IntPtr.Zero, "dying encoder: StartVodCapture returns a handle");
        if (api == IntPtr.Zero) return;

        byte[] frame = new byte[320 * 240 * 4]; // bigger than a pipe buffer
        bool rejected = false;
        var clock = Stopwatch.StartNew();
        while (!rejected && clock.Elapsed.TotalSeconds < 10)
            rejected = !LinuxFFmpegNative.CaptureVodFrames(api, frame, 1);
        log.IsTrue(rejected, "dying encoder: CaptureVodFrames returns false once ffmpeg is gone");
        log.IsTrue(!LinuxFFmpegNative.StopVodCapture(api), "dying encoder: StopVodCapture reports failure");
        LinuxFFmpegNative.CleanVodCapture(api);
    }

    static void FailedMux(Log log, string dir, string ffmpeg)
    {
        string video = Path.Combine(dir, "synthetic_auto_video.mp4"); // left by SyntheticTake
        string garbage = Path.Combine(dir, "not_audio.wav");
        string media = Path.Combine(dir, "failed_mux.mp4");
        File.WriteAllBytes(garbage, Encoding.ASCII.GetBytes("this is not a wav file"));
        DeleteIfExists(media);

        IntPtr mux = LinuxFFmpegNative.StartMuxingProcess(EncoderPreset.H264_MP4, Kbps, false, false,
            media, video, garbage, ffmpeg, false);
        log.IsTrue(mux == IntPtr.Zero, "failed mux: StartMuxingProcess returns IntPtr.Zero");
        log.IsTrue(!File.Exists(media), "failed mux: no partial output left behind");
        LinuxFFmpegNative.CleanMuxingProcess(mux);

        mux = LinuxFFmpegNative.StartMuxingProcess(EncoderPreset.H264_MP4, Kbps, false, false,
            media, video, Path.Combine(dir, "missing.wav"), ffmpeg, false);
        log.IsTrue(mux == IntPtr.Zero, "missing audio: StartMuxingProcess returns IntPtr.Zero");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    static void CheckRedTopBlackBottom(Log log, byte[] rgb, string what)
    {
        log.IsTrue(rgb != null && rgb.Length == Width * Height * 3, $"{what}: decoded a {Width}x{Height} frame");
        if (rgb == null || rgb.Length != Width * Height * 3) return;
        Color32 top = AverageRgb(rgb, Width / 2, Height / 4);
        Color32 bottom = AverageRgb(rgb, Width / 2, Height * 3 / 4);
        Debug.Log($"[LinuxRecorderSelfTest] {what}: decoded top = ({top.r},{top.g},{top.b}), bottom = ({bottom.r},{bottom.g},{bottom.b}).");
        log.IsTrue(top.r > 230 && top.g < 25 && top.b < 25,
            $"{what}: TOP half is red — got ({top.r},{top.g},{top.b}); blue = channel swap, black = upside down");
        log.IsTrue(bottom.r < 25 && bottom.g < 25 && bottom.b < 25,
            $"{what}: BOTTOM half is black — got ({bottom.r},{bottom.g},{bottom.b})");
    }

    // Mean of a 16x16 block centred on (cx, cy); rgb24 rows top-down.
    static Color32 AverageRgb(byte[] rgb, int cx, int cy)
    {
        int r = 0, g = 0, b = 0, n = 0;
        for (int y = cy - 8; y < cy + 8; y++)
            for (int x = cx - 8; x < cx + 8; x++)
            {
                int i = (y * Width + x) * 3;
                r += rgb[i]; g += rgb[i + 1]; b += rgb[i + 2]; n++;
            }
        return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
    }

    static byte[] DecodeFrameRgb24(string ffmpeg, string file, double atSeconds, int width, int height)
    {
        var info = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-hide_banner -nostdin -v error -ss {atSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                        $"-i \"{file}\" -frames:v 1 -f rawvideo -pix_fmt rgb24 -",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (Process p = Process.Start(info))
        using (var buffer = new MemoryStream(width * height * 3))
        {
            var stderr = p.StandardError.ReadToEndAsync();
            p.StandardOutput.BaseStream.CopyTo(buffer);
            p.WaitForExit();
            if (p.ExitCode != 0)
                Debug.LogError($"[LinuxRecorderSelfTest] decode failed: {stderr.Result}");
            return buffer.ToArray();
        }
    }

    [Serializable] class ProbeStream
    {
        public string codec_type; public string codec_name; public int width; public int height;
        public string color_space; public string color_primaries; public string color_transfer; public string color_range;
    }
    [Serializable] class ProbeFormat { public string duration; }
    [Serializable] class ProbeResult
    {
        public ProbeStream[] streams;
        public ProbeFormat format;

        public string Codec(string type)
        {
            foreach (ProbeStream s in streams ?? new ProbeStream[0])
                if (s.codec_type == type) return s.codec_name;
            return null;
        }

        public string VideoSize()
        {
            foreach (ProbeStream s in streams ?? new ProbeStream[0])
                if (s.codec_type == "video") return $"{s.width}x{s.height}";
            return null;
        }

        public ProbeStream Video()
        {
            foreach (ProbeStream s in streams ?? new ProbeStream[0])
                if (s.codec_type == "video") return s;
            return null;
        }

        public float Duration()
        {
            float d;
            return format != null && float.TryParse(format.duration, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out d) ? d : -1f;
        }
    }

    static ProbeResult Probe(string ffprobe, string file)
    {
        RunProcess(ffprobe, $"-v error -show_entries stream=codec_type,codec_name,width,height,color_space,color_primaries,color_transfer,color_range:format=duration " +
                            $"-of json \"{file}\"", out string stdout, out string stderr);
        Debug.Log($"[LinuxRecorderSelfTest] ffprobe {Path.GetFileName(file)}: {stdout}");
        return JsonUtility.FromJson<ProbeResult>(stdout) ?? new ProbeResult();
    }

    static int RunProcess(string file, string args, out string stdout, out string stderr)
    {
        var info = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (Process p = Process.Start(info))
        {
            var err = p.StandardError.ReadToEndAsync();
            stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            stderr = err.Result;
            return p.ExitCode;
        }
    }

    // 16-bit PCM — the format Evereal's AudioRecorder writes.
    static void WriteSineWav(string path, double seconds, int sampleRate, int channels, double hz)
    {
        int frames = (int)(seconds * sampleRate);
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int dataBytes = frames * channels * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(sampleRate); w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
            for (int i = 0; i < frames; i++)
            {
                short v = (short)(Math.Sin(2 * Math.PI * hz * i / sampleRate) * 0.3 * short.MaxValue);
                for (int c = 0; c < channels; c++) w.Write(v);
            }
        }
    }

    static void DeleteIfExists(params string[] paths)
    {
        foreach (string p in paths)
            if (File.Exists(p)) File.Delete(p);
    }

    class Log
    {
        readonly List<string> failures = new List<string>();
        int checks;

        public void IsTrue(bool condition, string what)
        {
            checks++;
            if (!condition) failures.Add(what);
        }

        public void AreEqual(object expected, object actual, string what)
        {
            checks++;
            if (!Equals(expected, actual))
                failures.Add($"{what} — expected {expected}, got {actual}");
        }

        public void Near(float expected, float actual, float tolerance, string what)
        {
            checks++;
            if (Mathf.Abs(expected - actual) > tolerance)
                failures.Add($"{what} — expected {expected:F3} ±{tolerance:F3}, got {actual:F3}");
        }

        public void Fail(string what)
        {
            checks++;
            failures.Add(what);
        }

        public bool Report()
        {
            if (failures.Count == 0)
            {
                Debug.Log($"[LinuxRecorderSelfTest] {checks} checks passed.");
                return true;
            }
            var sb = new StringBuilder();
            sb.Append($"[LinuxRecorderSelfTest] {failures.Count} of {checks} checks FAILED:\n");
            foreach (string f in failures) sb.Append("  • ").Append(f).Append('\n');
            Debug.LogError(sb.ToString());
            return false;
        }
    }
}
#endif
