#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > ET > Setup Curved Monitor UI
/// Maakt in één keer: layers, RenderTexture (3840x1200), scherm-materiaal (URP/Unlit),
/// UI-camera, canvas "MonitorUI" met 3 panelen, en koppelt CurvedScreen + MeshCollider
/// aan het object "Monitor Screen". Veilig om opnieuw te draaien (hergebruikt wat al bestaat).
/// Zet dit bestand in een map met de naam "Editor".
/// </summary>
public static class MonitorUISetup
{
    // ---- raster: 1600 px per meter, scherm 2,40 x 0,75 m ----
    const int W = 3840, H = 1200;
    const int MARGIN = 40, GUTTER = 40, PANEL_H = H - 2 * MARGIN;   // 1120
    const int SIDE_W = 1320, MID_W = 1040;                            // 40+1320+40+1040+40+1320+40 = 3840

    const string UI_LAYER = "ScreenUI";
    const string SCREEN_LAYER = "CurvedScreen";
    const string RT_PATH = "Assets/_Project/Textures/MonitorUI_RT.renderTexture";
    const string MAT_PATH = "Assets/_Project/Materials/MonitorUI_Screen.mat";
    const string SCREEN_OBJECT = "Monitor Screen";

    [MenuItem("Tools/ET/Setup Curved Monitor UI")]
    public static void Setup()
    {
        // 1. Layers
        int uiLayer = EnsureLayer(UI_LAYER);
        int screenLayer = EnsureLayer(SCREEN_LAYER);

        // 2. RenderTexture
        EnsureFolder(Path.GetDirectoryName(RT_PATH));
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RT_PATH);
        if (rt == null)
        {
            rt = new RenderTexture(W, H, 24)
            {
                name = "MonitorUI_RT",
                useMipMap = true,          // scherper/rustiger op afstand in VR
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                antiAliasing = 1
            };
            AssetDatabase.CreateAsset(rt, RT_PATH);
        }

        // 3. Materiaal voor het scherm
        EnsureFolder(Path.GetDirectoryName(MAT_PATH));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { Debug.LogError("URP/Unlit shader niet gevonden."); return; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MAT_PATH);
        }
        mat.SetTexture("_BaseMap", rt);
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);

        // 4. UI-camera (ver weg, rendert alleen layer ScreenUI naar de RT)
        var camGO = GameObject.Find("Monitor UI Camera");
        if (camGO == null)
        {
            camGO = new GameObject("Monitor UI Camera");
            Undo.RegisterCreatedObjectUndo(camGO, "Monitor UI Camera");
        }
        camGO.transform.SetPositionAndRotation(new Vector3(0, -100, 0), Quaternion.identity);
        var uiCam = GetOrAdd<Camera>(camGO);
        uiCam.orthographic = true;
        uiCam.clearFlags = CameraClearFlags.SolidColor;
        uiCam.backgroundColor = Color.black;
        uiCam.cullingMask = 1 << uiLayer;
        uiCam.targetTexture = rt;
        uiCam.nearClipPlane = 0.1f;
        uiCam.farClipPlane = 10f;
        uiCam.depth = -10;

        // XR-camera mag de verborgen canvas niet zien
        foreach (var c in Object.FindObjectsByType<Camera>())
            if (c != uiCam && c.targetTexture == null)
            {
                Undo.RecordObject(c, "Exclude ScreenUI");
                c.cullingMask &= ~(1 << uiLayer);
            }

        // 5. Canvas + 3 panelen
        var canvasGO = GameObject.Find("MonitorUI");
        if (canvasGO == null)
        {
            canvasGO = new GameObject("MonitorUI", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasGO, "MonitorUI");
        }
        var canvas = GetOrAdd<Canvas>(canvasGO);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCam;
        canvas.planeDistance = 1f;

        var scaler = GetOrAdd<CanvasScaler>(canvasGO);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(W, H);
        scaler.matchWidthOrHeight = 0.5f;

        var raycaster = GetOrAdd<GraphicRaycaster>(canvasGO);

        int x = MARGIN;
        Panel(canvasGO.transform, "Panel_MirrorCam", x, SIDE_W); x += SIDE_W + GUTTER;
        Panel(canvasGO.transform, "Panel_DPad", x, MID_W);       x += MID_W + GUTTER;
        Panel(canvasGO.transform, "Panel_HUD", x, SIDE_W);
        // De dartboard (3D) hangt rechtstreeks onder Panel_HUD; geen HUD Feed-RawImage nodig.

        SetLayerRecursive(canvasGO, uiLayer);

        // 6. Het gebogen scherm koppelen
        var screenGO = GameObject.Find(SCREEN_OBJECT);
        if (screenGO == null)
        {
            Debug.LogWarning($"'{SCREEN_OBJECT}' niet gevonden in de scene. Stap 6 overgeslagen.");
        }
        else
        {
            Undo.RecordObject(screenGO, "Setup screen");
            screenGO.layer = screenLayer;

            var mr = screenGO.GetComponent<MeshRenderer>();
            if (mr) { Undo.RecordObject(mr, "Screen material"); mr.sharedMaterial = mat; }

            var mf = screenGO.GetComponent<MeshFilter>();
            var mc = GetOrAdd<MeshCollider>(screenGO);
            mc.convex = false;
            if (mf) mc.sharedMesh = mf.sharedMesh;

            var cs = GetOrAdd<CurvedScreen>(screenGO);
            cs.uiCamera = uiCam;
            cs.raycaster = raycaster;
            EditorUtility.SetDirty(cs);

            // Read/Write aan op de FBX (nodig voor textureCoord in builds)
            if (mf && mf.sharedMesh)
            {
                var path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                    Debug.Log($"Read/Write aangezet op {path}");
                }
            }
        }

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = canvasGO;
        Debug.Log("Curved monitor UI klaar. Verhuis nu de D-pad, mirror cam en HUD naar de panelen.");
    }

    // Geen null-coalescing met Unity-objecten (nep-null), daarom expliciet:
    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (!c) c = Undo.AddComponent<T>(go);
        return c;
    }

    static Transform Panel(Transform parent, string name, int x, int w)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            t = go.transform;
            t.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 1f);
        }
        var r = (RectTransform)t;
        r.anchorMin = r.anchorMax = Vector2.zero;   // linksonder
        r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(x, MARGIN);
        r.sizeDelta = new Vector2(w, PANEL_H);
        return t;
    }

    static int EnsureLayer(string name)
    {
        int l = LayerMask.NameToLayer(name);
        if (l != -1) return l;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = name;
                tm.ApplyModifiedProperties();
                return i;
            }
        }
        throw new System.Exception("Geen vrije layer meer over.");
    }

    static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }
}
#endif
