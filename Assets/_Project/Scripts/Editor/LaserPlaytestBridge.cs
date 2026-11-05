using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets a tool outside Unity (Claude Code) start an automated play test without touching the editor: it watches
/// Logs/Playtest/request and acts on what it says.
///   refresh                 import changed scripts and assets (= what Unity does when you click on its window)
///   play [levels=1,2] [nodir] [noplay]   enter Play Mode; LaserPlaytestBot plays, writes report.md and stops Play Mode
///   menu Tools/...          run an editor menu item (scene changes are not saved)
///   stop                    leave Play Mode
/// Logs/Playtest/bridge.txt says what the bridge last did. Logs/ is not in git.
/// </summary>
[InitializeOnLoad]
static class LaserPlaytestBridge
{
    static double s_NextPoll;

    static string Folder => LaserPlaytestBot.Folder;
    static string RequestFile => Path.Combine(Folder, "request");

    static LaserPlaytestBridge()
    {
        Directory.CreateDirectory(Folder);
        Status("ready (scripts compiled " + System.DateTime.Now.ToString("HH:mm:ss") + ")");
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < s_NextPoll) return;
        s_NextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(RequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        string command = File.ReadAllText(RequestFile).Trim();
        File.Delete(RequestFile);

        if (command.StartsWith("refresh"))
        {
            Status("refresh");
            AssetDatabase.Refresh();
        }
        else if (command.StartsWith("play"))
        {
            if (EditorApplication.isPlaying)
            {
                Status("busy: already in Play Mode, request ignored");
                return;
            }

            File.Delete(Path.Combine(Folder, "done"));
            File.WriteAllText(LaserPlaytestBot.AutorunFile, command);
            Status("play: " + command);
            EditorApplication.isPlaying = true;
        }
        else if (command.StartsWith("menu "))
        {
            // Runs an editor menu item, e.g. "menu Tools/Einstein Telescope/Look/Report prop materials"
            string item = command.Substring(5).Trim();
            bool ok = EditorApplication.ExecuteMenuItem(item);
            Status((ok ? "menu done: " : "menu NOT found: ") + item);
        }
        else if (command.StartsWith("stop"))
        {
            Status("stop");
            EditorApplication.isPlaying = false;
        }
        else
        {
            Status("unknown request: " + command);
        }
    }

    static void Status(string text)
    {
        File.WriteAllText(Path.Combine(Folder, "bridge.txt"), System.DateTime.Now.ToString("HH:mm:ss") + " " + text);
    }
}
