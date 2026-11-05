using System.Collections.Generic;
using System.Text;
using EinsteinTelescope.Laser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Einstein Telescope > Monitor > Remove LASER and IR VIEWER buttons
/// The players always use the visible green 532 nm alignment laser, so the LASER (532 / 1064 nm) and
/// IR VIEWER buttons on the control pad (Panel_DPad > Pad Canvas) are no longer needed.
/// This removes them from the open scene (with Undo), clears their label references on the
/// LaserControlPad, and re-centres the buttons left in their row with the original spacing.
/// In Room_Scene the bottom row was RESET | SHUTTER | LASER | IR VIEWER (x = -213, -71, 71, 213);
/// afterwards it is RESET | SHUTTER (x = -71, 71). Safe to run again: it does nothing if the buttons are gone.
/// Save the scene afterwards.
/// </summary>
public static class MonitorButtonCleanup
{
    // Buttons whose anchored y differs less than this (in canvas pixels) are in the same row.
    const float RowTolerance = 1f;

    [MenuItem("Tools/Einstein Telescope/Monitor/Remove LASER and IR VIEWER buttons")]
    static void RemoveButtons()
    {
        var allButtons = Object.FindObjectsByType<LaserPadButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        var retired = new List<LaserPadButton>();
        foreach (var button in allButtons)
        {
            if (LaserControlPad.IsRetiredAction(button.Action)) retired.Add(button);
        }

        if (retired.Count == 0)
        {
            Debug.Log("[MonitorButtonCleanup] No LASER or IR VIEWER buttons found in the open scene. Nothing to do.");
            return;
        }

        Undo.SetCurrentGroupName("Remove LASER and IR VIEWER buttons");
        int undoGroup = Undo.GetCurrentGroup();
        var report = new StringBuilder("[MonitorButtonCleanup] Removed the LASER and IR VIEWER buttons.\n");
        var dirtyScenes = new HashSet<UnityEngine.SceneManagement.Scene>();

        // 1. Work out the new layout of each affected row before anything is removed.
        var newPositions = new List<(RectTransform rect, Vector2 position)>();
        var handledRows = new List<(Transform parent, float y)>();
        foreach (var button in retired)
        {
            if (!(button.transform is RectTransform rect) || rect.parent == null) continue;

            float rowY = rect.anchoredPosition.y;
            bool alreadyHandled = false;
            foreach (var row in handledRows)
            {
                if (row.parent == rect.parent && Mathf.Abs(row.y - rowY) < RowTolerance) alreadyHandled = true;
            }
            if (alreadyHandled) continue;
            handledRows.Add((rect.parent, rowY));

            PlanRow(rect.parent, rowY, allButtons, newPositions, report);
        }

        // 2. Clear the label references on the control pad (they point into the buttons being removed).
        foreach (var pad in Object.FindObjectsByType<LaserControlPad>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(pad);
            ClearReference(so, "wavelengthText");
            ClearReference(so, "infraredText");
            so.ApplyModifiedProperties();
            dirtyScenes.Add(pad.gameObject.scene);
        }

        // 3. Remove the buttons (their labels are children and go with them).
        foreach (var button in retired)
        {
            GameObject go = button.gameObject;
            dirtyScenes.Add(go.scene);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                // Objects inside a prefab instance cannot be destroyed in the scene: switch them off instead.
                Undo.RecordObject(go, "Hide retired button");
                go.SetActive(false);
                report.AppendLine("  Switched off (part of a prefab instance, remove it in Prefab Mode): " + go.name);
            }
            else
            {
                report.AppendLine("  Removed: " + go.name + " (" + button.Action + ")");
                Undo.DestroyObjectImmediate(go);
            }
        }

        // 4. Move the remaining buttons of the row.
        foreach (var (rect, position) in newPositions)
        {
            Undo.RecordObject(rect, "Re-centre pad buttons");
            rect.anchoredPosition = position;
            dirtyScenes.Add(rect.gameObject.scene);
        }

        Undo.CollapseUndoOperations(undoGroup);

        foreach (var scene in dirtyScenes)
        {
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }

        report.Append("Save the scene (Ctrl+S) to keep this.");
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Plans the new x positions for the buttons that stay in one row: same spacing as before, centred on
    /// the middle of the original row.
    /// </summary>
    static void PlanRow(Transform parent, float rowY, LaserPadButton[] allButtons,
                       List<(RectTransform rect, Vector2 position)> newPositions, StringBuilder report)
    {
        var rowAll = new List<RectTransform>();
        var rowKept = new List<RectTransform>();
        foreach (var button in allButtons)
        {
            if (!(button.transform is RectTransform rect) || rect.parent != parent) continue;
            if (Mathf.Abs(rect.anchoredPosition.y - rowY) >= RowTolerance) continue;

            rowAll.Add(rect);
            if (!LaserControlPad.IsRetiredAction(button.Action)) rowKept.Add(rect);
        }

        rowAll.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));
        rowKept.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));

        if (rowKept.Count == 0 || rowAll.Count < 2)
        {
            report.AppendLine("  Row at y = " + rowY + " under '" + parent.name + "': nothing to re-centre.");
            return;
        }

        // Spacing: the smallest distance between neighbours in the original row.
        float spacing = float.MaxValue;
        for (int i = 1; i < rowAll.Count; i++)
        {
            spacing = Mathf.Min(spacing, rowAll[i].anchoredPosition.x - rowAll[i - 1].anchoredPosition.x);
        }
        if (spacing <= 0f || spacing == float.MaxValue) spacing = rowKept[0].sizeDelta.x + 10f;

        float centre = (rowAll[0].anchoredPosition.x + rowAll[rowAll.Count - 1].anchoredPosition.x) * 0.5f;

        report.Append("  Row at y = " + rowY + " under '" + parent.name + "' (spacing " + spacing + "):");
        for (int i = 0; i < rowKept.Count; i++)
        {
            float x = centre + (i - (rowKept.Count - 1) * 0.5f) * spacing;
            newPositions.Add((rowKept[i], new Vector2(x, rowKept[i].anchoredPosition.y)));
            report.Append("  " + rowKept[i].name + " " + rowKept[i].anchoredPosition.x + " -> " + x);
        }
        report.AppendLine();
    }

    static void ClearReference(SerializedObject so, string propertyName)
    {
        var property = so.FindProperty(propertyName);
        if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
        {
            property.objectReferenceValue = null;
        }
    }
}
