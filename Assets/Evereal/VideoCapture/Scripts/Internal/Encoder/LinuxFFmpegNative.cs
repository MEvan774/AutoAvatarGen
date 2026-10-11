/* AutoAvatarGen addition — NOT part of the Evereal package. Re-apply after an Evereal update. */

// LINUX: managed stand-in for Evereal's native "FFmpegEncoder" library, which
// ships only as Plugins/x86*/FFmpegEncoder.dll (Windows) and
// FFmpegEncoder.bundle (macOS). On Linux, FFmpegEncoder.cs and FFmpegMuxer.cs
// route their [DllImport] names to the methods below (every such edit is tagged
// "// LINUX:"), so everything above the native layer — VideoCapture's status and
// events, the frame queue and its pacing, the muxer thread, the audio_*.wav
// intermediate, RecordingSession's watchdog/salvage — runs unchanged.
//
// The contract, as the callers use it:
//  * Frames are Unity readback bytes: RGBA32 (4 bytes/pixel, R first), rows
//    BOTTOM-UP — Texture2D.GetRawTextureData of Utils.CreateTexture's RGBA32
//    texture, or AsyncGPUReadback of the ARGB32 capture RenderTexture.
//    FFmpegEncoder passes verticalFlip = true for exactly that row order.
//  * bitrate is kbps (CaptureBase: "Video bitrate in Kbps").
//  * Start* returns IntPtr.Zero on failure. CaptureVodFrames returns false when
//    the frame did not reach ffmpeg (FFmpegEncoder then un-counts it); `count`
//    is how many times to repeat the frame to hold the frame rate. Stop returns
//    exit code == 0 (FFmpegEncoder ignores it; the log does not).
//  * Muxing is synchronous: FFmpegMuxer.StartMux already runs on its own thread
//    and decides success by File.Exists(mediaPath) afterwards, so a failed mux
//    must not leave a partial file behind.
#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Evereal.VideoCapture
{
  public static class LinuxFFmpegNative
  {
    private const string LOG_FORMAT = "[LinuxFFmpeg] {0}";

    // Readback layout — see the header.
    private const string InputPixelFormat = "rgba";
    private const int BytesPerPixel = 4;

    // H.264 encoder for H264_MP4 takes: "auto" (default) encodes on the NVIDIA
    // GPU (h264_nvenc) when a probe at the start of each take shows this ffmpeg
    // and driver can open it, and falls back to libx264 on the CPU otherwise.
    // Force one with H264Encoder or the environment variable below
    // ("h264_nvenc" | "libx264" | "auto").
    //
    // Measured on the render machine (i7-11800H + GTX 1650, 20 s of real 1080p
    // footage through this exact rgba -> BT.709 chain at 8000k): libx264
    // veryfast keeps 3.5 CPU cores busy at realtime, NVENC p5 0.6 cores, at the
    // same quality (SSIM 0.9728 vs 0.9727) and file size.
    public const string H264EncoderEnv = "AUTOAVATARGEN_H264_ENCODER";
    public static string H264Encoder = null; // null -> H264EncoderEnv -> "auto"

    // The encoder the most recent H.264 take used (for logs and the self-test).
    public static string LastH264Encoder { get; private set; }

    // libx264 speed/quality trade-off for the CPU fallback. The bitrate is fixed
    // by the caller, so this only decides how hard x264 works for it. Measured
    // as above: medium 37 fps — too close to 30 once Unity shares the CPU —
    // faster 58, veryfast 80.
    public static string H264Preset = "veryfast";

    // An NVENC probe is one black frame; it normally takes ~0.4 s.
    private const int NvencProbeTimeoutMs = 15000;

    // If ffmpeg dies this fast it never got past option parsing (bad path, an
    // encoder this ffmpeg build lacks) — report it as a failed start.
    private const int EarlyExitCheckMs = 150;

    #region VOD capture

    public static IntPtr StartVodCapture(
      EncoderPreset preset,
      int width,
      int height,
      int bitrate,
      int frameRate,
      bool verticalFlip,
      bool horizontalFlip,
      ProjectionType projectionType,
      StereoMode stereoMode,
      string videoPath,
      string ffmpegPath)
    {
      if (width <= 0 || height <= 0 || frameRate <= 0 || string.IsNullOrEmpty(videoPath))
      {
        Debug.LogErrorFormat(LOG_FORMAT, $"Invalid VOD settings: {width}x{height} @ {frameRate}fps -> '{videoPath}'.");
        return IntPtr.Zero;
      }

      string h264Encoder = IsH264(preset) ? ChooseH264Encoder(preset, width, height, bitrate, frameRate, ffmpegPath) : null;
      string pixFmt;
      string codec = VideoCodecArgs(preset, bitrate, h264Encoder, out pixFmt);

      var filters = new List<string>();
      if (verticalFlip) filters.Add("vflip");
      if (horizontalFlip) filters.Add("hflip");
      if (pixFmt != null) filters.Add(YuvFilters(pixFmt));

      string args =
        "-hide_banner -nostats -loglevel info -y " +
        $"-f rawvideo -pix_fmt {InputPixelFormat} -video_size {width}x{height} -framerate {frameRate} -i - " +
        (filters.Count > 0 ? $"-vf {string.Join(",", filters)} " : "") +
        codec + " " +
        Quote(videoPath);

      FFmpegProcess session = FFmpegProcess.Launch(ffmpegPath, args, true, "VOD encode", videoPath);
      if (session == null)
        return IntPtr.Zero;

      session.frameBytes = width * height * BytesPerPixel;
      session.frameRate = frameRate;
      return GCHandle.ToIntPtr(GCHandle.Alloc(session));
    }

    public static bool CaptureVodFrames(IntPtr api, byte[] data, int count)
    {
      FFmpegProcess session = Get(api);
      if (session == null || data == null)
        return false;
      return session.WriteFrame(data, count);
    }

    public static unsafe bool CaptureVodFrames(IntPtr api, byte* data, int count)
    {
      FFmpegProcess session = Get(api);
      if (session == null || data == null)
        return false;
      if (session.scratch == null)
        session.scratch = new byte[session.frameBytes];
      Marshal.Copy((IntPtr)data, session.scratch, 0, session.frameBytes);
      return session.WriteFrame(session.scratch, count);
    }

    public static bool StopVodCapture(IntPtr api)
    {
      FFmpegProcess session = Get(api);
      if (session == null)
        return false;
      return session.Finish();
    }

    public static void CleanVodCapture(IntPtr api)
    {
      Release(api);
    }

    #endregion

    #region Muxing

    public static IntPtr StartMuxingProcess(
      EncoderPreset preset,
      int bitrate,
      bool verticalFlip,
      bool horizontalFlip,
      string mediaPath,
      string videoPath,
      string audioPath,
      string ffmpegPath,
      bool isLive)
    {
      return Mux(preset, bitrate, verticalFlip, horizontalFlip, mediaPath, videoPath,
        new[] { audioPath }, ffmpegPath, isLive);
    }

    public static IntPtr StartMuxingProcess2(
      EncoderPreset preset,
      int bitrate,
      bool verticalFlip,
      bool horizontalFlip,
      string mediaPath,
      string videoPath,
      string audioPath,
      string audioPath2,
      string ffmpegPath,
      bool isLive)
    {
      return Mux(preset, bitrate, verticalFlip, horizontalFlip, mediaPath, videoPath,
        new[] { audioPath, audioPath2 }, ffmpegPath, isLive);
    }

    public static void CleanMuxingProcess(IntPtr api)
    {
      Release(api);
    }

    private static IntPtr Mux(
      EncoderPreset preset,
      int bitrate,
      bool verticalFlip,
      bool horizontalFlip,
      string mediaPath,
      string videoPath,
      string[] audioPaths,
      string ffmpegPath,
      bool isLive)
    {
      if (isLive)
        return Unsupported("Live-stream muxing");

      if (!File.Exists(videoPath))
      {
        Debug.LogErrorFormat(LOG_FORMAT, $"Mux: video file not found: '{videoPath}'.");
        return IntPtr.Zero;
      }
      foreach (string audio in audioPaths)
      {
        if (!File.Exists(audio))
        {
          Debug.LogErrorFormat(LOG_FORMAT, $"Mux: audio file not found: '{audio}'.");
          return IntPtr.Zero;
        }
      }

      var args = new StringBuilder("-hide_banner -nostdin -nostats -loglevel info -y ");
      args.Append("-i ").Append(Quote(videoPath)).Append(' ');
      foreach (string audio in audioPaths)
        args.Append("-i ").Append(Quote(audio)).Append(' ');

      args.Append("-map 0:v:0 ");
      if (audioPaths.Length == 1)
        args.Append("-map 1:a:0 ");
      else
        args.Append("-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[aout]\" -map \"[aout]\" ");

      if (verticalFlip || horizontalFlip)
      {
        // The capture is already encoded; a flip at mux time means a re-encode.
        var flips = new List<string>();
        if (verticalFlip) flips.Add("vflip");
        if (horizontalFlip) flips.Add("hflip");
        // CPU encode: a flip at mux time is a rare, post-take step.
        string pixFmt;
        args.Append("-vf ").Append(string.Join(",", flips)).Append(' ')
            .Append(VideoCodecArgs(preset, bitrate, "libx264", out pixFmt)).Append(' ');
      }
      else
      {
        args.Append("-c:v copy ");
      }
      args.Append("-c:a aac -b:a 192k ").Append(Quote(mediaPath));

      FFmpegProcess mux = FFmpegProcess.Launch(ffmpegPath, args.ToString(), false, "Mux", mediaPath);
      if (mux == null || !mux.Finish())
      {
        // FFmpegMuxer treats File.Exists(mediaPath) as success.
        try { if (File.Exists(mediaPath)) File.Delete(mediaPath); }
        catch (Exception e) { Debug.LogWarningFormat(LOG_FORMAT, $"Could not remove partial mux output '{mediaPath}': {e.Message}"); }
        if (mux != null) mux.Dispose();
        return IntPtr.Zero;
      }
      return GCHandle.ToIntPtr(GCHandle.Alloc(mux));
    }

    #endregion

    #region Not supported on Linux

    // Live streaming and screenshots are not used by AutoAvatarGen; their
    // natives fail with a clear error instead of a DllNotFoundException.
    public static IntPtr Unsupported(string feature)
    {
      Debug.LogErrorFormat(LOG_FORMAT,
        feature + " is not supported on Linux: Evereal's native FFmpegEncoder plugin does not exist " +
        "for this platform, and only VOD capture and muxing are implemented by LinuxFFmpegNative.");
      return IntPtr.Zero;
    }

    #endregion

    #region Internal

    // Matched by name: the enum is #if'd per platform (H264_NVIDIA_MP4 exists
    // whenever a Windows build target is active, even in a Linux editor).
    private static bool IsH264(EncoderPreset preset)
    {
      string name = preset.ToString();
      return name.StartsWith("H264") && !name.Contains("LOSSLESS");
    }

    // The codec arguments, and the pixel format the codec is fed (null for an
    // RGB codec, which gets no YUV conversion).
    private static string VideoCodecArgs(EncoderPreset preset, int bitrate, string h264Encoder, out string pixFmt)
    {
      string name = preset.ToString();
      pixFmt = "yuv420p";
      if (name.Contains("LOSSLESS"))
      {
        pixFmt = "yuv444p";
        return "-c:v libx264 -preset ultrafast -qp 0";
      }
      if (name.StartsWith("H265"))
        return $"-c:v libx265 -b:v {bitrate}k -tag:v hvc1";
      if (name.StartsWith("VP8"))
        return $"-c:v libvpx -b:v {bitrate}k";
      if (name.StartsWith("VP9"))
        return $"-c:v libvpx-vp9 -b:v {bitrate}k";
      if (name.StartsWith("FFV1"))
      {
        pixFmt = null;
        return "-c:v ffv1 -level 3";
      }
      // H264_MP4 (and H264_NVIDIA_MP4, see IsH264).
      if (h264Encoder == "h264_nvenc")
        return $"-c:v h264_nvenc -preset p5 -tune hq -rc vbr -b:v {bitrate}k -maxrate {bitrate * 3 / 2}k -bufsize {bitrate * 2}k";
      return $"-c:v libx264 -preset {H264Preset} -b:v {bitrate}k";
    }

    // RGB -> YUV the way HD video is decoded: BT.709 matrix, limited range —
    // swscale's default is BT.601, i.e. shifted colours — and the frames TAGGED
    // bt709 (setparams). With this ffmpeg (8.1) the -color_primaries/-color_trc
    // output options came out "unknown"; tags set on the frames survive.
    // Converting here (not inside NVENC, which would use BT.601 for RGB input)
    // keeps both encoders' output identical in colour.
    private static string YuvFilters(string pixFmt)
    {
      return "scale=out_color_matrix=bt709:out_range=tv,format=" + pixFmt +
             ",setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv";
    }

    private static string ChooseH264Encoder(
      EncoderPreset preset, int width, int height, int bitrate, int frameRate, string ffmpegPath)
    {
      string wanted = (H264Encoder ?? Environment.GetEnvironmentVariable(H264EncoderEnv) ?? "auto")
        .Trim().ToLowerInvariant();
      if (wanted == "libx264" || wanted == "h264_nvenc")
      {
        Debug.LogFormat(LOG_FORMAT, $"H.264 encoder forced to {wanted}.");
        return LastH264Encoder = wanted;
      }
      if (wanted != "auto" && wanted.Length > 0)
        Debug.LogWarningFormat(LOG_FORMAT, $"Unknown H.264 encoder '{wanted}' ({H264EncoderEnv}) — using auto.");

      // One black frame through the exact NVENC settings the take will use.
      string pixFmt;
      string codec = VideoCodecArgs(preset, bitrate, "h264_nvenc", out pixFmt);
      string probeArgs = "-hide_banner -nostdin -v error " +
        $"-f lavfi -i color=c=black:s={width}x{height}:r={frameRate},format={InputPixelFormat} " +
        $"-frames:v 1 -vf {YuvFilters(pixFmt)} {codec} -f null -";
      string failure = RunProbe(ffmpegPath, probeArgs);
      if (failure == null)
      {
        Debug.LogFormat(LOG_FORMAT, "H.264 encoder: h264_nvenc (NVIDIA GPU).");
        return LastH264Encoder = "h264_nvenc";
      }
      Debug.LogWarningFormat(LOG_FORMAT, $"NVENC unavailable ({failure}) — H.264 encoder: libx264 (CPU).");
      return LastH264Encoder = "libx264";
    }

    // Null when ffmpeg ran the probe successfully, else why it did not.
    private static string RunProbe(string ffmpegPath, string args)
    {
      var startInfo = new ProcessStartInfo
      {
        FileName = ffmpegPath,
        Arguments = args,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      };
      try
      {
        using (Process probe = Process.Start(startInfo))
        {
          var stderr = probe.StandardError.ReadToEndAsync();
          probe.StandardOutput.ReadToEndAsync();
          if (!probe.WaitForExit(NvencProbeTimeoutMs))
          {
            try { probe.Kill(); } catch (Exception) { /* already gone */ }
            return $"probe timed out after {NvencProbeTimeoutMs / 1000}s";
          }
          if (probe.ExitCode == 0)
            return null;
          string said = stderr.Wait(2000) ? stderr.Result.Trim().Replace('\n', ' ') : "";
          return $"exit {probe.ExitCode}: {said}";
        }
      }
      catch (Exception e)
      {
        return e.Message;
      }
    }

    private static string Quote(string s)
    {
      return "\"" + s.Replace("\"", "\\\"") + "\"";
    }

    private static FFmpegProcess Get(IntPtr api)
    {
      if (api == IntPtr.Zero)
        return null;
      try
      {
        return GCHandle.FromIntPtr(api).Target as FFmpegProcess;
      }
      catch (InvalidOperationException)
      {
        return null;
      }
    }

    private static void Release(IntPtr api)
    {
      if (api == IntPtr.Zero)
        return;
      GCHandle handle;
      try
      {
        handle = GCHandle.FromIntPtr(api);
      }
      catch (InvalidOperationException)
      {
        return;
      }
      if (!handle.IsAllocated)
        return;
      var session = handle.Target as FFmpegProcess;
      handle.Free();
      if (session != null)
        session.Dispose();
    }

    /// <summary>
    /// One ffmpeg child. stdin (video sessions only) is fed by the caller;
    /// stdout and stderr are drained on background threads for the whole run,
    /// or ffmpeg would block mid-take on a full pipe.
    /// </summary>
    private sealed class FFmpegProcess : IDisposable
    {
      private const int TailLines = 40;

      public int frameBytes;
      public int frameRate;
      public byte[] scratch;

      private Process process;
      private StreamWriter stdinWriter; // closed through the writer, or Process.Dispose throws later
      private Stream stdin;
      private string label;
      private string outputPath;
      private Thread stderrThread;
      private Thread stdoutThread;
      private readonly Queue<string> stderrTail = new Queue<string>();
      private bool broken;
      private bool finished;
      private bool finishedOk;

      // Performance counters for the end-of-take summary.
      private long framesWritten;
      private long uniqueFrames;
      private readonly Stopwatch wallClock = new Stopwatch();
      private readonly Stopwatch blockedOnPipe = new Stopwatch();

      public static FFmpegProcess Launch(string ffmpegPath, string args, bool feedStdin, string label, string outputPath)
      {
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
        {
          Debug.LogErrorFormat(LOG_FORMAT, $"{label}: ffmpeg not found at '{ffmpegPath}'.");
          return null;
        }

        var session = new FFmpegProcess { label = label, outputPath = outputPath };
        var startInfo = new ProcessStartInfo
        {
          FileName = ffmpegPath,
          Arguments = args,
          UseShellExecute = false,
          CreateNoWindow = true,
          RedirectStandardInput = feedStdin,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
        };

        Debug.LogFormat(LOG_FORMAT, $"{label}: {ffmpegPath} {args}");
        try
        {
          session.process = Process.Start(startInfo);
        }
        catch (Exception e)
        {
          Debug.LogErrorFormat(LOG_FORMAT, $"{label}: could not start '{ffmpegPath}': {e.Message}");
          return null;
        }
        if (session.process == null)
        {
          Debug.LogErrorFormat(LOG_FORMAT, $"{label}: could not start '{ffmpegPath}'.");
          return null;
        }
        session.wallClock.Start();

        session.stderrThread = session.Drain(session.process.StandardError, true);
        session.stdoutThread = session.Drain(session.process.StandardOutput, false);

        if (feedStdin)
        {
          session.stdinWriter = session.process.StandardInput;
          session.stdin = session.stdinWriter.BaseStream;
          if (session.process.WaitForExit(EarlyExitCheckMs))
          {
            session.JoinDrains();
            Debug.LogErrorFormat(LOG_FORMAT,
              $"{label}: ffmpeg exited immediately (code {session.process.ExitCode}).\n{session.Tail()}");
            session.Dispose();
            return null;
          }
        }
        return session;
      }

      private Thread Drain(StreamReader reader, bool keepTail)
      {
        var thread = new Thread(() =>
        {
          try
          {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
              if (!keepTail) continue;
              lock (stderrTail)
              {
                stderrTail.Enqueue(line);
                while (stderrTail.Count > TailLines) stderrTail.Dequeue();
              }
            }
          }
          catch (Exception)
          {
            // Stream torn down with the process — nothing left to drain.
          }
        });
        thread.IsBackground = true;
        thread.Name = "LinuxFFmpeg " + (keepTail ? "stderr" : "stdout");
        thread.Start();
        return thread;
      }

      public bool WriteFrame(byte[] data, int count)
      {
        if (broken || finished || stdin == null)
          return false;
        if (data.Length < frameBytes)
        {
          Debug.LogErrorFormat(LOG_FORMAT, $"{label}: frame is {data.Length} bytes, expected {frameBytes} — dropped.");
          return false;
        }
        if (count <= 0)
          return true;

        blockedOnPipe.Start();
        try
        {
          for (int i = 0; i < count; i++)
            stdin.Write(data, 0, frameBytes);
          stdin.Flush();
        }
        catch (Exception e)
        {
          broken = true;
          // Let ffmpeg finish dying so its last words are in the tail.
          process.WaitForExit(2000);
          JoinDrains();
          Debug.LogErrorFormat(LOG_FORMAT,
            $"{label}: ffmpeg stopped accepting frames after {framesWritten} ({e.GetType().Name}: {e.Message}).\n{Tail()}");
          return false;
        }
        finally
        {
          blockedOnPipe.Stop();
        }
        framesWritten += count;
        uniqueFrames++;
        return true;
      }

      /// <summary>Close stdin and wait for ffmpeg to finish writing. No fixed
      /// timeout: a long take legitimately needs a while to flush.</summary>
      public bool Finish()
      {
        if (finished)
          return finishedOk;
        finished = true;

        if (stdinWriter != null)
        {
          try { stdinWriter.Close(); }
          catch (Exception) { /* broken pipe — reported by WriteFrame */ }
        }
        process.WaitForExit();
        JoinDrains();
        wallClock.Stop();

        int exitCode = process.ExitCode;
        bool outputExists = string.IsNullOrEmpty(outputPath) || File.Exists(outputPath);
        finishedOk = exitCode == 0 && outputExists && !broken;

        if (finishedOk)
        {
          string stats = stdin == null ? "" :
            $" {framesWritten} frames encoded ({uniqueFrames} captured, {framesWritten - uniqueFrames} repeated to hold " +
            $"{frameRate}fps); {blockedOnPipe.Elapsed.TotalSeconds:F1}s of {wallClock.Elapsed.TotalSeconds:F1}s spent " +
            "waiting on ffmpeg to accept frames.";
          Debug.LogFormat(LOG_FORMAT, $"{label} finished -> '{outputPath}'.{stats}\n{Tail()}");
        }
        else
        {
          Debug.LogErrorFormat(LOG_FORMAT,
            $"{label} FAILED (exit {exitCode}, output {(outputExists ? "exists" : "missing")}).\n{Tail()}");
        }
        return finishedOk;
      }

      private void JoinDrains()
      {
        if (stderrThread != null) stderrThread.Join(5000);
        if (stdoutThread != null) stdoutThread.Join(5000);
      }

      private string Tail()
      {
        lock (stderrTail)
        {
          return "ffmpeg: " + string.Join("\nffmpeg: ", stderrTail.ToArray());
        }
      }

      public void Dispose()
      {
        if (process == null)
          return;
        try
        {
          if (!process.HasExited)
          {
            Debug.LogWarningFormat(LOG_FORMAT, $"{label}: ffmpeg still running at cleanup — killing it.");
            process.Kill();
          }
        }
        catch (Exception)
        {
          // Already gone.
        }
        try { process.Dispose(); }
        catch (Exception) { /* streams already torn down */ }
        process = null;
      }
    }

    #endregion
  }
}

#endif
