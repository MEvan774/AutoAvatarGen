/* Copyright (c) 2019-present Evereal. All rights reserved. */

using System.IO;
using UnityEngine;

namespace Evereal.VideoCapture
{
  /// <summary>
  /// FFmpeg executable utility.
  /// </summary>
  public class FFmpegConfig
  {
    // The ffmpeg folder
    public static string folder
    {
      get
      {
        return Application.streamingAssetsPath + "/FFmpeg/";
      }
    }

    // The 32bit ffmpeg path for Windows
    public static string windows32Path
    {
      get
      {
        return folder + "x86/ffmpeg.exe";
      }
    }

    // The 32bit ffmpeg path for Windows
    public static string windows64Path
    {
      get
      {
        return folder + "x86_64/ffmpeg.exe";
      }
    }

    // The ffmpeg path for macOS
    public static string macOSPath
    {
      get
      {
        return folder + "ffmpeg";
      }
    }

    // Get ffmpeg path
    public static string path
    {
      get
      {
#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR) // LINUX: checked first — a Linux editor with a Windows target defines UNITY_STANDALONE_WIN
        return linuxPath;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (Utils.isWindows64Bit())
        {
          return windows64Path;
        }
        return windows32Path;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        return macOSPath;
#else
        return "";
#endif
      }
    }

    public static bool IsExist()
    {
      return File.Exists(path);
    }

#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
    // LINUX: the system ffmpeg (no binary is bundled — StreamingAssets/FFmpeg/ffmpeg
    // is the macOS one). Resolved, as a FULL path because IsExist() uses
    // File.Exists, in this order:
    //   1. PlayerPref AutoAvatarGen.FfmpegPath — the key MugsTech.Tts.FfmpegRunner
    //      uses, so one setting drives TTS and recording (a bare name is looked
    //      up on PATH);
    //   2. StreamingAssets/FFmpeg/linux/ffmpeg, if one is ever bundled;
    //   3. ffmpeg on PATH.
    private const string LinuxPathPrefKey = "AutoAvatarGen.FfmpegPath";
    private static readonly string[] LinuxFallbackDirs = { "/usr/bin", "/usr/local/bin", "/bin" };
    private static string linuxCachedPath;
    private static string linuxRejectedPref;

    public static string linuxBundledPath
    {
      get
      {
        return folder + "linux/ffmpeg";
      }
    }

    private static string linuxPath
    {
      get
      {
        string pref;
        try
        {
          pref = PlayerPrefs.GetString(LinuxPathPrefKey, "").Trim();
        }
        catch (System.Exception)
        {
          // PlayerPrefs is main-thread only; off it, reuse the last answer.
          return linuxCachedPath ?? "";
        }

        string resolved = ResolveLinuxExecutable(pref);
        if (resolved == null && pref.Length > 0 && pref != linuxRejectedPref)
        {
          linuxRejectedPref = pref;
          Debug.LogWarning("[FFmpegConfig] PlayerPref " + LinuxPathPrefKey + " = '" + pref +
                           "' is not a usable ffmpeg — falling back to the bundled/PATH ffmpeg.");
        }
        if (resolved == null) resolved = ResolveLinuxExecutable(linuxBundledPath);
        if (resolved == null) resolved = ResolveLinuxExecutable("ffmpeg");
        if (resolved == null) resolved = "";

        if (resolved != linuxCachedPath)
        {
          if (resolved.Length > 0)
            Debug.Log("[FFmpegConfig] Using ffmpeg at '" + resolved + "'.");
          else
            Debug.LogError("[FFmpegConfig] No ffmpeg found: install it (e.g. dnf install ffmpeg) or set PlayerPref " +
                           LinuxPathPrefKey + " to its full path.");
          linuxCachedPath = resolved;
        }
        return resolved;
      }
    }

    // A full path to an existing, non-Windows/macOS executable, or null.
    private static string ResolveLinuxExecutable(string candidate)
    {
      if (string.IsNullOrEmpty(candidate))
        return null;

      if (candidate.IndexOf('/') >= 0)
      {
        string full = Path.GetFullPath(candidate);
        return File.Exists(full) && !IsForeignBinary(full) ? full : null;
      }

      var dirs = new System.Collections.Generic.List<string>(
        (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'));
      dirs.AddRange(LinuxFallbackDirs);
      foreach (string dir in dirs)
      {
        if (string.IsNullOrEmpty(dir) || !Path.IsPathRooted(dir))
          continue;
        string full = Path.Combine(dir, candidate);
        if (File.Exists(full) && !IsForeignBinary(full))
          return full;
      }
      return null;
    }

    // Mach-O (the bundled macOS ffmpeg) or a Windows PE image can never run here.
    private static bool IsForeignBinary(string file)
    {
      try
      {
        var magic = new byte[4];
        using (var stream = File.OpenRead(file))
        {
          if (stream.Read(magic, 0, 4) < 2)
            return false;
        }
        if (magic[0] == 'M' && magic[1] == 'Z')
          return true;
        uint be = (uint)(magic[0] << 24 | magic[1] << 16 | magic[2] << 8 | magic[3]);
        return be == 0xFEEDFACE || be == 0xFEEDFACF || be == 0xCEFAEDFE || be == 0xCFFAEDFE || be == 0xCAFEBABE;
      }
      catch (System.Exception)
      {
        return true;
      }
    }
#endif

    public static void CheckFolder()
    {
      if (!Directory.Exists(folder))
      {
        Directory.CreateDirectory(folder);
      }
      string win64Folder = folder + "x86_64/";
      if (!Directory.Exists(win64Folder))
      {
        Directory.CreateDirectory(win64Folder);
      }
      string win32Folder = folder + "x86/";
      if (!Directory.Exists(win32Folder))
      {
        Directory.CreateDirectory(win32Folder);
      }
    }
  }
}