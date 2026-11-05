using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Einstein Telescope > XR > Clean up Composition Layers leftovers
/// After removing Unity OpenXR Meta / Android XR / Composition Layers, things are left behind
/// that point to scripts that no longer exist:
///  1. OpenXR features of those packages in OpenXRPackageSettings.asset
///  2. "missing script" components in the open scenes (e.g. CompositionLayer on Global Volume)
///  3. the empty object "Default Scene Layer" that Composition Layers created itself
/// Safe to run again. Save the scene afterwards.
/// </summary>
public static class XRCleanup
{
    const string OPENXR_SETTINGS = "Assets/XR/Settings/OpenXRPackageSettings.asset";
    const string LEFTOVER_LAYER_OBJECT = "Default Scene Layer";

    [MenuItem("Tools/Einstein Telescope/XR/Clean up Composition Layers leftovers")]
    static void Cleanup()
    {
        // 1. Feature sub-assets without a script, then remove empty slots from the feature lists
        int features = AssetDatabase.RemoveScriptableObjectsWithMissingScript(OPENXR_SETTINGS);
        int listEntries = 0;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(OPENXR_SETTINGS))
        {
            // By name, so this script does not depend on the OpenXR assembly
            if (!obj || obj.GetType().Name != "OpenXRSettings") continue;
            var so = new SerializedObject(obj);
            var list = so.FindProperty("features");
            if (list == null || !list.isArray) continue;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue) continue;
                list.DeleteArrayElementAtIndex(i);
                listEntries++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (features > 0 || listEntries > 0) AssetDatabase.SaveAssets();

        // 2 + 3. Open scenes
        int components = 0, objects = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            bool changed = false;

            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing == 0) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    Debug.LogWarning($"[XRCleanup] '{go.name}' is a prefab instance; clean up the missing scripts in Prefab Mode.", go);
                    continue;
                }
                Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                components += missing;
                changed = true;
            }

            // Only remove it if nothing is left on it after step 2 (just a Transform, no children)
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != LEFTOVER_LAYER_OBJECT) continue;
                if (root.transform.childCount > 0 || root.GetComponents<Component>().Length > 1) continue;
                Undo.DestroyObjectImmediate(root);
                objects++;
                changed = true;
            }

            if (changed) EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[XRCleanup] Removed {features} OpenXR features and {listEntries} empty references from {OPENXR_SETTINGS}, " +
                  $"{components} missing scripts and {objects} '{LEFTOVER_LAYER_OBJECT}' object(s) from the open scene(s). " +
                  "Save the scene.");
    }
}
