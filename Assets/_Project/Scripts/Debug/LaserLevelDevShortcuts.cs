using EinsteinTelescope.Laser;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Developer shortcuts for testing the level flow quickly. Creates itself on Play (only in the editor and in
/// development builds), so nothing is needed in the scene.
///   F6   complete the current level now (results screen on the monitor, stars, personal best)
///   F7   put every mirror of the current level on its designed angle (0, 0). If the level then does not complete
///        within a few seconds, the optics are not on one line: the Console prints the objective report.
/// </summary>
public class LaserLevelDevShortcuts : MonoBehaviour
{
    const float CHECK_AFTER_SECONDS = 6f;

    LaserLevelManager levelManager;
    LaserAlignmentObjective objective;
    float checkAt = -1f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("Laser Level Dev Shortcuts");
        DontDestroyOnLoad(go);
        go.AddComponent<LaserLevelDevShortcuts>();
    }
#endif

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!levelManager) levelManager = FindAnyObjectByType<LaserLevelManager>();
        if (!objective) objective = FindAnyObjectByType<LaserAlignmentObjective>();
        if (!levelManager || !objective) return;

        if (keyboard.f6Key.wasPressedThisFrame)
        {
            Debug.Log("[LaserLevelDevShortcuts] F6: level " + (levelManager.CurrentLevelIndex + 1) + " completed by shortcut.");
            objective.ForceComplete();
        }

        if (keyboard.f7Key.wasPressedThisFrame && levelManager.CurrentLevel != null && !objective.IsCompleted)
        {
            foreach (MirrorMount mirror in levelManager.CurrentLevel.Mirrors)
            {
                if (mirror) mirror.ResetToNominal();
            }

            if (objective.IsWaitingToStart) objective.StartObjective();
            checkAt = Time.time + CHECK_AFTER_SECONDS;
            Debug.Log("[LaserLevelDevShortcuts] F7: all mirrors of level " + (levelManager.CurrentLevelIndex + 1) +
                      " on their designed angle. The level should complete within a few seconds.");
        }

        if (checkAt >= 0f && Time.time >= checkAt)
        {
            checkAt = -1f;
            if (!objective.IsCompleted)
            {
                Debug.LogWarning("[LaserLevelDevShortcuts] With all mirrors at their designed angle the level is NOT complete. " +
                                 "Check the targets below (tolerance, power, beam count) and the optics positions.\n" +
                                 objective.BuildStatusReport());
            }
        }
    }
}
