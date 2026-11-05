using EinsteinTelescope.Laser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts a "VIEW" button on the control pad, under NEXT (same spot as UNDO sits under PREV). The button is a copy of
/// the UNDO button, so it has the same look, hover highlight and raycast path (LaserPadButton → LaserControlPad).
/// It cycles the camera views of <see cref="LaserMirrorCamera"/>. Safe to run again: an existing VIEW button is reused.
/// </summary>
public static class MirrorCameraViewButtonSetup
{
    const string ButtonName = "View Camera";

    [MenuItem("Tools/Einstein Telescope/Laser/Mirror camera: Add VIEW button", priority = 40)]
    static void AddButton()
    {
        LaserPadButton undo = null, next = null, previous = null, view = null;
        foreach (var b in Object.FindObjectsByType<LaserPadButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            switch (b.Action)
            {
                case LaserPadAction.Undo: undo = b; break;
                case LaserPadAction.NextMirror: next = b; break;
                case LaserPadAction.PreviousMirror: previous = b; break;
                case LaserPadAction.NextCameraView: view = b; break;
            }
        }

        if (!undo || !next || !previous)
        {
            Debug.LogError("[MirrorCameraViewButtonSetup] UNDO, PREV or NEXT button not found in the scene.");
            return;
        }

        var undoRect = (RectTransform)undo.transform;
        var nextRect = (RectTransform)next.transform;
        var previousRect = (RectTransform)previous.transform;

        RectTransform viewRect;
        if (view)
        {
            viewRect = (RectTransform)view.transform;
        }
        else
        {
            var copy = Object.Instantiate(undo.gameObject, undoRect.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Add VIEW button");
            copy.name = ButtonName;
            viewRect = (RectTransform)copy.transform;

            var so = new SerializedObject(copy.GetComponent<LaserPadButton>());
            so.FindProperty("action").enumValueIndex = (int)LaserPadAction.NextCameraView;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Under NEXT, at the same distance as UNDO under PREV
        viewRect.anchorMin = undoRect.anchorMin;
        viewRect.anchorMax = undoRect.anchorMax;
        viewRect.pivot = undoRect.pivot;
        viewRect.sizeDelta = undoRect.sizeDelta;
        viewRect.anchoredPosition = nextRect.anchoredPosition + (undoRect.anchoredPosition - previousRect.anchoredPosition);
        viewRect.localScale = undoRect.localScale;
        viewRect.localRotation = undoRect.localRotation;
        viewRect.anchoredPosition3D = new Vector3(viewRect.anchoredPosition.x, viewRect.anchoredPosition.y, undoRect.anchoredPosition3D.z);

        var label = viewRect.GetComponentInChildren<Text>(true);
        if (label) label.text = "VIEW";

        // An earlier version put the button on the camera screen's world canvas, which is not on the monitor
        var mirrorCamera = Object.FindFirstObjectByType<LaserMirrorCamera>(FindObjectsInactive.Include);
        if (mirrorCamera)
        {
            var camSo = new SerializedObject(mirrorCamera);
            var feed = camSo.FindProperty("feedImage").objectReferenceValue as RawImage;
            var old = feed ? feed.transform.parent.Find("View Button") : null;
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var viewLabel = camSo.FindProperty("viewLabel");
            if (viewLabel != null) viewLabel.objectReferenceValue = null;
            camSo.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(viewRect.gameObject.scene);
        Selection.activeGameObject = viewRect.gameObject;
        Debug.Log("[MirrorCameraViewButtonSetup] VIEW button placed under NEXT. Save the scene.");
    }
}
