using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Einstein Telescope > XR > Restanten Composition Layers opruimen
/// Na het verwijderen van Unity OpenXR Meta / Android XR / Composition Layers blijven er dingen achter
/// die naar niet-bestaande scripts wijzen:
///  1. OpenXR-features van die packages in OpenXRPackageSettings.asset
///  2. "missing script"-componenten in de open scenes (bijv. CompositionLayer op Global Volume)
///  3. het lege object "Default Scene Layer" dat Composition Layers zelf aanmaakte
/// Veilig om opnieuw te draaien. Sla daarna de scene op.
/// </summary>
public static class XRCleanup
{
    const string OPENXR_SETTINGS = "Assets/XR/Settings/OpenXRPackageSettings.asset";
    const string LEFTOVER_LAYER_OBJECT = "Default Scene Layer";

    [MenuItem("Tools/Einstein Telescope/XR/Restanten Composition Layers opruimen")]
    static void Cleanup()
    {
        // 1. Feature-subassets zonder script, daarna lege plekken uit de featurelijsten halen
        int features = AssetDatabase.RemoveScriptableObjectsWithMissingScript(OPENXR_SETTINGS);
        int listEntries = 0;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(OPENXR_SETTINGS))
        {
            // Op naam, zodat dit script niet afhangt van de OpenXR-assembly
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
                    Debug.LogWarning($"[XRCleanup] '{go.name}' is een prefab-instantie; ruim de missing scripts op in Prefab Mode.", go);
                    continue;
                }
                Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                components += missing;
                changed = true;
            }

            // Alleen weghalen als er na stap 2 niets meer op zit (alleen een Transform, geen kinderen)
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

        Debug.Log($"[XRCleanup] {features} OpenXR-features en {listEntries} lege verwijzingen uit {OPENXR_SETTINGS} gehaald, " +
                  $"{components} missing scripts en {objects} '{LEFTOVER_LAYER_OBJECT}'-object(en) uit de open scene(s). " +
                  "Sla de scene op.");
    }
}
