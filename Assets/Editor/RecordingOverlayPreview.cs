using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders RecordingSession's full-screen "recording in progress" overlay to
/// PNGs — one per phase (preparing / recording / wrapping up) at 1920x1080 and
/// 1280x800 — so its design can be reviewed without recording a take. The
/// overlay is drawn over a magenta background: any magenta in a PNG is a pixel
/// the overlay failed to cover, and that pixel would flicker during a take.
///
///   Menu:  MugsTech ▸ Recording ▸ Render Progress Overlay Previews
///   Batch: Unity -batchmode -quit -projectPath &lt;project&gt; -logFile &lt;log&gt;
///            -executeMethod RecordingOverlayPreview.RenderFromCommandLine [-previewOut &lt;dir&gt;]
/// </summary>
public static class RecordingOverlayPreview
{
    const string OutDirArg = "-previewOut";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static readonly (string name, int phase, float percent)[] States =
    {
        ("preparing", 0, 0f),
        ("recording", 1, 42.3f),
        ("wrapping_up", 2, 100f),
    };
    static readonly Vector2Int[] Sizes = { new Vector2Int(1920, 1080), new Vector2Int(1280, 800) };
    const float PreviewClipSeconds = 478f;

    [MenuItem("MugsTech/Recording/Render Progress Overlay Previews")]
    static void RenderFromMenu()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AutoAvatarGen_OverlayPreview");
        Render(dir);
        EditorUtility.RevealInFinder(dir);
    }

    public static void RenderFromCommandLine()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AutoAvatarGen_OverlayPreview");
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == OutDirArg) dir = Path.GetFullPath(args[i + 1]);
        EditorApplication.Exit(Render(dir) ? 0 : 1);
    }

    static bool Render(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Type type = typeof(RecordingSession);
        Type phaseType = type.GetNestedType("IndicatorPhase", BindingFlags.NonPublic);

        // Awake doesn't run in edit mode, so nothing but the overlay is built.
        var host = new GameObject("OverlayPreviewHost") { hideFlags = HideFlags.HideAndDontSave };
        var session = host.AddComponent<RecordingSession>();
        type.GetMethod("BuildIndicator", Private).Invoke(session, null);
        var root = (GameObject)type.GetField("indicatorRoot", Private).GetValue(session);
        root.hideFlags = HideFlags.HideAndDontSave;

        // Draw through a camera into a texture instead of onto the screen.
        var cam = new GameObject("OverlayPreviewCamera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.magenta;
        cam.cullingMask = 1 << 5; // UI
        root.layer = 5;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        var scaler = root.GetComponent<CanvasScaler>();
        MethodInfo handleScale = typeof(CanvasScaler).GetMethod("Handle", Private);

        bool ok = true;
        try
        {
            foreach (Vector2Int size in Sizes)
            {
                var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                foreach (var state in States)
                {
                    type.GetField("displayedPercent", Private).SetValue(session, state.percent);
                    type.GetField("clipLengthSec", Private).SetValue(session, PreviewClipSeconds);
                    type.GetField("shownPercent", Private).SetValue(session, -1);
                    var ring = (Image)type.GetField("progressRing", Private).GetValue(session);
                    ring.fillAmount = state.percent * 0.01f;
                    var arc = (RectTransform)type.GetField("activityArc", Private).GetValue(session);
                    arc.localRotation = Quaternion.Euler(0f, 0f, -65f);
                    var takeName = (Text)type.GetField("takeNameText", Private).GetValue(session);
                    takeName.text = state.phase == 0 ? "" : "COLD_OPEN_STITCHED_2026-10-11_14-42-17";
                    type.GetMethod("ApplyIndicatorPhase", Private)
                        .Invoke(session, new[] { Enum.ToObject(phaseType, state.phase) });

                    handleScale.Invoke(scaler, null);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    int uncovered = 0;
                    foreach (Color32 c in tex.GetPixels32())
                        if (c.r > 200 && c.g < 60 && c.b > 200) uncovered++;
                    if (uncovered > 0) ok = false;

                    string file = Path.Combine(outDir, $"overlay_{state.name}_{size.x}x{size.y}.png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    Debug.Log($"[RecordingOverlayPreview] {file} — {(uncovered == 0 ? "fully covered" : uncovered + " UNCOVERED pixels")}.");
                }
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(host);
        }
        return ok;
    }
}
