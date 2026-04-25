using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Drives the main menu scene: Start Recording (launches the recording scene
/// via RecordingSession.Begin) and Quit. On return from a recording, reads
/// RecordingSession.LastResult and shows success / failure plus the saved
/// file path.
///
/// Also exposes a text field for the Python pre-processor output folder.
/// ScriptFileReader.Start() reads the same PlayerPrefs key and overrides its
/// own <c>pythonOutputFolder</c> so the user can point the pipeline at a
/// different output directory without editing the SampleScene.
///
/// The UI itself lives as authored GameObjects in MainMenu.unity. Re-build
/// the hierarchy with: Tools -> AutoAvatarGen -> Build Main Menu UI
/// (see Assets/Editor/MainMenuUIBuilder.cs).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // Shared with ScriptFileReader. If you rename this, rename it there too.
    public const string PythonOutputFolderPrefKey = "AutoAvatarGen.PythonOutputFolder";
    public const string DefaultPythonOutputFolder = "Python/output";

    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text pathText;
    [SerializeField] TMP_InputField pathInput;
    [SerializeField] Button startButton;
    [SerializeField] Button quitButton;

    void Awake()
    {
        startButton.onClick.AddListener(OnStartClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
        pathInput.onEndEdit.AddListener(OnPathChanged);
        pathInput.text = PlayerPrefs.GetString(PythonOutputFolderPrefKey, DefaultPythonOutputFolder);
        RefreshResult();
    }

    void OnEnable()
    {
        RecordingSession.ResultChanged += RefreshResult;
    }

    void OnDisable()
    {
        RecordingSession.ResultChanged -= RefreshResult;
    }

    void RefreshResult()
    {
        var r = RecordingSession.LastResult;
        if (r == null)
        {
            statusText.text  = "Ready to record.";
            statusText.color = new Color(0.82f, 0.85f, 0.90f, 1f);
            pathText.text    = "No recording has been completed yet in this session.";
            return;
        }

        switch (r.State)
        {
            case RecordingSession.RecordingResult.Status.Generating:
                statusText.text  = "●  Recording complete";
                statusText.color = new Color(0.98f, 0.80f, 0.30f, 1f);
                pathText.text    = "Generating video… Evereal is finalising the file, " +
                                   "this usually takes a few seconds.";
                break;

            case RecordingSession.RecordingResult.Status.Saved:
                statusText.text  = "✓  Video saved";
                statusText.color = new Color(0.35f, 0.85f, 0.45f, 1f);
                pathText.text    = string.IsNullOrEmpty(r.SavePath) ? "(no path returned)" : r.SavePath;
                break;

            case RecordingSession.RecordingResult.Status.Failed:
                statusText.text  = "✗  Recording failed";
                statusText.color = new Color(0.95f, 0.35f, 0.35f, 1f);
                pathText.text    = string.IsNullOrEmpty(r.ErrorMessage) ? "(no error details)" : r.ErrorMessage;
                break;
        }
    }

    void OnStartClicked()
    {
        // Flush the current path value in case the user typed into the input
        // but didn't click out of it before hitting Start.
        OnPathChanged(pathInput.text);
        RecordingSession.Begin();
    }

    void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnPathChanged(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? DefaultPythonOutputFolder : value.Trim();
        PlayerPrefs.SetString(PythonOutputFolderPrefKey, trimmed);
        PlayerPrefs.Save();
        if (pathInput.text != trimmed) pathInput.text = trimmed;
    }
}
