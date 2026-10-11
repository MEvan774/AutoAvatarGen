using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the player the MugsTech pipeline runs (Linux:
/// <c>AutoAvatarGen.x86_64 --auto-script ...</c>, Windows: <c>AutoAvatarGen.exe</c>),
/// from the scenes and player settings exactly as configured in the project —
/// the same result as File ▸ Build Settings ▸ Build. The executable name fixes
/// the data folder's name (AutoAvatarGen_Data/).
///
///   Menu:  MugsTech ▸ Build ▸ Linux Player… / Windows Player… (asks for the folder)
///   Batch (editor closed) — builds for the active target, so pass -buildTarget:
///     Unity -batchmode -quit -projectPath &lt;project&gt; -logFile &lt;log&gt; -buildTarget Linux64
///           -executeMethod PlayerBuild.BuildFromCommandLine -buildPath &lt;folder&gt;
///
/// Exit code 0 on success, 1 on a failed build, 2 on a missing argument.
/// </summary>
public static class PlayerBuild
{
    const string BuildPathArg = "-buildPath";
    const string WindowsExeName = "AutoAvatarGen.exe";
    const string LinuxExeName   = "AutoAvatarGen.x86_64";

    [MenuItem("MugsTech/Build/Linux Player…")]
    static void BuildLinuxFromMenu()
    {
        string folder = EditorUtility.SaveFolderPanel("Build AutoAvatarGen (Linux x86_64) into…", "", "Build");
        if (string.IsNullOrEmpty(folder)) return;
        Build(folder, BuildTarget.StandaloneLinux64);
    }

    [MenuItem("MugsTech/Build/Windows Player…")]
    static void BuildFromMenu()
    {
        string folder = EditorUtility.SaveFolderPanel("Build AutoAvatarGen (Windows x64) into…", "", "Build");
        if (string.IsNullOrEmpty(folder)) return;
        Build(folder, BuildTarget.StandaloneWindows64);
    }

    public static void BuildFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        string folder = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], BuildPathArg, StringComparison.OrdinalIgnoreCase))
                folder = args[i + 1];
        if (string.IsNullOrWhiteSpace(folder))
        {
            Debug.LogError($"[PlayerBuild] Missing {BuildPathArg} <folder>.");
            EditorApplication.Exit(2);
            return;
        }
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64
            ? BuildTarget.StandaloneWindows64
            : BuildTarget.StandaloneLinux64;
        bool ok = Build(Path.GetFullPath(folder), target);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Build(string folder, BuildTarget target)
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[PlayerBuild] No enabled scenes in Build Settings.");
            return false;
        }
        Directory.CreateDirectory(folder);

        var options = new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = Path.Combine(folder,
                target == BuildTarget.StandaloneLinux64 ? LinuxExeName : WindowsExeName),
            target           = target,
            options          = BuildOptions.None,
        };
        Debug.Log($"[PlayerBuild] Building {scenes.Length} scene(s) -> '{options.locationPathName}'.");
        BuildReport  report  = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[PlayerBuild] OK: '{summary.outputPath}' " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}s, " +
                      $"{summary.totalWarnings} warning(s)).");
            return true;
        }
        Debug.LogError($"[PlayerBuild] {summary.result}: {summary.totalErrors} error(s), " +
                       $"{summary.totalWarnings} warning(s).");
        return false;
    }
}
