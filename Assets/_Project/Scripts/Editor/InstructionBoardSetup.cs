using EinsteinTelescope.Laser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EinsteinTelescope.Props.Editor
{
    /// <summary>
    /// Menu: Tools > Einstein Telescope > Instructiebord > Briefing op bord zetten.
    /// Puts the existing laser briefing (LaserBriefingPanel, "Briefing Screen (floating)") exactly on the
    /// screen of the wall TV (InstructionBoard_Screen) and gives that screen a dark background.
    /// Nothing else changes: the level manager, HELP and the X/START buttons open and close the briefing as before.
    /// Run it again after moving the TV: it re-fits everything. Undo (Ctrl+Z) reverts it.
    /// </summary>
    public static class InstructionBoardSetup
    {
        private const string ScreenName = "InstructionBoard_Screen";
        private const string DarkScreenMaterial = "ET2_BoardScreen_Active";
        private const float Fill = 0.98f;          // share of the screen the briefing uses
        private const float SurfaceOffset = 0.004f; // canvas in front of the screen (m), avoids z-fighting

        [MenuItem("Tools/Einstein Telescope/Instructiebord/Briefing op bord zetten")]
        public static void FitBriefingToBoard()
        {
            MeshRenderer screen = FindScreen();
            if (screen == null)
            {
                EditorUtility.DisplayDialog("Instructiebord",
                    "Geen '" + ScreenName + "' gevonden in de scene.\nZet ET_ControlRoom_Props in de scene (of selecteer de TV) en probeer opnieuw.", "OK");
                return;
            }

            LaserBriefingPanel briefing = FindBriefing();
            if (briefing == null)
            {
                EditorUtility.DisplayDialog("Instructiebord",
                    "Geen LaserBriefingPanel in deze scene. Zet eerst de laser-demo / Carlo's prefabs in de scene.", "OK");
                return;
            }

            if (!ScreenFrame(screen, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size))
            {
                EditorUtility.DisplayDialog("Instructiebord", "Kon de vorm van het scherm niet lezen (mesh niet leesbaar?).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Briefing in de TV");
            int group = Undo.GetCurrentGroup();

            // 1. Briefing root onto the screen. Canvas front faces -Z, so its forward points into the wall.
            Transform root = briefing.transform;
            Undo.RecordObject(root, "Move briefing");
            root.SetPositionAndRotation(center + normal * SurfaceOffset, Quaternion.LookRotation(-normal, up));

            // 2. Widen the canvas to the TV's aspect ratio, then scale it so it fills the screen.
            var so = new SerializedObject(briefing);
            var screenObject = so.FindProperty("screen").objectReferenceValue as GameObject;
            if (screenObject != null && screenObject.transform is RectTransform canvasRect)
            {
                Undo.RecordObject(canvasRect, "Fit briefing");
                MatchAspect(canvasRect, size.x / size.y);
                float scale = Mathf.Min(size.x * Fill / canvasRect.sizeDelta.x, size.y * Fill / canvasRect.sizeDelta.y);
                float parentScale = Mathf.Max(1e-6f, root.lossyScale.x);
                canvasRect.localPosition = Vector3.zero;
                canvasRect.localRotation = Quaternion.identity;
                canvasRect.localScale = Vector3.one * (scale / parentScale);
            }

            // 3. Dark TV screen behind the briefing (also what you see while the briefing is closed).
            Material dark = FindMaterial(DarkScreenMaterial);
            if (dark != null)
            {
                Undo.RecordObject(screen, "TV screen material");
                screen.sharedMaterial = dark;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            Selection.activeGameObject = briefing.gameObject;
            Debug.Log($"Instructiebord: briefing in de TV gezet ({size.x:0.00} x {size.y:0.00} m).", briefing);
        }

        private const string BriefingLayerName = "BriefingUI";
        private const string BriefingCameraName = "Briefing UI Camera";
        private const string BriefingRtPath = "Assets/_Project/Textures/Room/BriefingTV_RT.renderTexture";
        private const string BriefingMatPath = "Assets/_Project/Materials/Room/BriefingTV_Screen.mat";
        private const int BriefingRtWidth = 2048;

        /// <summary>
        /// Shows the briefing on the TV through a RenderTexture with mipmaps and anisotropic filtering, like the curved monitor.
        /// Text drawn straight into the world (a world-space canvas) is sampled from a glyph atlas without mipmaps: seen from a
        /// distance, the GPU skips texels and the text shimmers. In a mipmapped RenderTexture the same text is filtered.
        /// The canvas itself stays exactly where it is (so the XR rays still hit the BACK/NEXT buttons) but moves to its own
        /// layer that the player's cameras do not draw; a camera in front of it renders it into the texture, and the TV shows
        /// that texture. Run it again after 'Briefing op bord zetten'. Undo (Ctrl+Z) reverts most of it.
        /// </summary>
        [MenuItem("Tools/Einstein Telescope/Instructiebord/Briefing via render texture (rustige tekst)")]
        public static void BriefingViaRenderTexture()
        {
            MeshRenderer screen = FindScreen();
            LaserBriefingPanel briefing = FindBriefing();
            if (screen == null || briefing == null)
            {
                EditorUtility.DisplayDialog("Instructiebord", "TV-scherm of briefing niet gevonden. Draai eerst 'Briefing op bord zetten'.", "OK");
                return;
            }

            var so = new SerializedObject(briefing);
            var screenObject = so.FindProperty("screen").objectReferenceValue as GameObject;
            var canvasRect = screenObject != null ? screenObject.transform as RectTransform : null;
            var canvas = screenObject != null ? screenObject.GetComponentInParent<Canvas>(true) : null;
            if (canvasRect == null || canvas == null)
            {
                EditorUtility.DisplayDialog("Instructiebord", "De briefing heeft geen canvas.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Briefing via render texture");
            int group = Undo.GetCurrentGroup();

            // 1. Own layer; the player's cameras do not draw it
            int layer = EnsureBriefingLayer();
            SetLayerRecursive(canvas.gameObject, layer);
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (c.targetTexture == null && c.name != BriefingCameraName)
                {
                    Undo.RecordObject(c, "Exclude BriefingUI");
                    c.cullingMask &= ~(1 << layer);
                }
            }

            // 2. RenderTexture with the canvas's aspect ratio, mipmapped and filtered
            float width = canvasRect.rect.width * canvasRect.lossyScale.x;
            float height = canvasRect.rect.height * canvasRect.lossyScale.y;
            int rtWidth = BriefingRtWidth;
            int rtHeight = Mathf.Max(64, Mathf.RoundToInt(rtWidth * height / width));
            EnsureFolder(System.IO.Path.GetDirectoryName(BriefingRtPath));
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(BriefingRtPath);
            if (rt != null && (rt.width != rtWidth || rt.height != rtHeight))
            {
                rt.Release();
                rt.width = rtWidth;
                rt.height = rtHeight;
                EditorUtility.SetDirty(rt);
            }
            else if (rt == null)
            {
                rt = new RenderTexture(rtWidth, rtHeight, 24)
                {
                    name = "BriefingTV_RT",
                    useMipMap = true,
                    autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 8,
                    antiAliasing = 1
                };
                AssetDatabase.CreateAsset(rt, BriefingRtPath);
            }

            // 3. Camera in front of the canvas, looking at it (the canvas front faces its own -Z)
            var cameraObject = GameObject.Find(BriefingCameraName);
            if (cameraObject == null)
            {
                cameraObject = new GameObject(BriefingCameraName);
                Undo.RegisterCreatedObjectUndo(cameraObject, "Briefing UI Camera");
            }

            Vector3 centre = canvasRect.TransformPoint(canvasRect.rect.center);
            cameraObject.transform.SetPositionAndRotation(centre - canvasRect.forward * 0.5f, Quaternion.LookRotation(canvasRect.forward, canvasRect.up));
            cameraObject.transform.SetParent(briefing.transform, true);
            var camera = cameraObject.GetComponent<Camera>();
            if (camera == null)
            {
                camera = Undo.AddComponent<Camera>(cameraObject);
            }

            camera.orthographic = true;
            camera.orthographicSize = height * 0.5f;
            camera.aspect = width / height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1f);
            camera.cullingMask = 1 << layer;
            camera.targetTexture = rt;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -20;

            // 4. Glyphs rasterised 1:1 for the texture; the texture's mipmaps do the filtering
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null)
            {
                Undo.RecordObject(scaler, "Briefing glyph resolution");
                scaler.dynamicPixelsPerUnit = Mathf.Max(1f, rtWidth / canvasRect.rect.width);
            }

            // 5. TV screen: unlit material with the texture (new material, the dark one stays untouched)
            EnsureFolder(System.IO.Path.GetDirectoryName(BriefingMatPath));
            var material = AssetDatabase.LoadAssetAtPath<Material>(BriefingMatPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) { Debug.LogError("URP/Unlit shader niet gevonden."); return; }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, BriefingMatPath);
            }

            material.SetTexture("_BaseMap", rt);
            material.SetColor("_BaseColor", Color.white);
            string uvNote = FitUv(screen, material);
            EditorUtility.SetDirty(material);

            Undo.RecordObject(screen, "TV screen material");
            screen.sharedMaterial = material;

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            Debug.Log($"Instructiebord: briefing via render texture ({rtWidth}x{rtHeight}, mipmaps, aniso 8). Canvas op layer '{BriefingLayerName}', glyph-resolutie {(scaler != null ? scaler.dynamicPixelsPerUnit : 0f):0.00}. {uvNote} Sla de scene op.", briefing);
        }

        /// <summary>Flips the texture when the TV mesh's UVs run against the screen's right/up, so text is not mirrored or upside down.</summary>
        private static string FitUv(MeshRenderer screen, Material material)
        {
            var filter = screen.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.vertexCount < 3 || !ScreenFrame(screen, out _, out Vector3 normal, out Vector3 up, out _))
            {
                return "(UV's niet gelezen, controleer of de tekst goed staat.)";
            }

            Vector3 right = Vector3.Cross(up, -normal);
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Transform t = screen.transform;
            float meanR = 0f, meanU = 0f, meanA = 0f, meanB = 0f;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            var r = new float[v.Length];
            var a = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = t.TransformPoint(v[i]);
                r[i] = Vector3.Dot(w, right);
                a[i] = Vector3.Dot(w, up);
                meanR += r[i]; meanA += a[i]; meanU += uv[i].x; meanB += uv[i].y;
                minU = Mathf.Min(minU, uv[i].x); maxU = Mathf.Max(maxU, uv[i].x);
                minV = Mathf.Min(minV, uv[i].y); maxV = Mathf.Max(maxV, uv[i].y);
            }

            meanR /= v.Length; meanA /= v.Length; meanU /= v.Length; meanB /= v.Length;
            float uR = 0f, uA = 0f, vR = 0f, vA = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                uR += (uv[i].x - meanU) * (r[i] - meanR);
                uA += (uv[i].x - meanU) * (a[i] - meanA);
                vR += (uv[i].y - meanB) * (r[i] - meanR);
                vA += (uv[i].y - meanB) * (a[i] - meanA);
            }

            if (Mathf.Abs(uR) < Mathf.Abs(uA) || Mathf.Abs(vA) < Mathf.Abs(vR))
            {
                return "WAARSCHUWING: de UV's van de TV staan 90 graden gedraaid, de tekst staat waarschijnlijk op zijn kant.";
            }

            float rangeU = Mathf.Max(1e-5f, maxU - minU), rangeV = Mathf.Max(1e-5f, maxV - minV);
            bool flipU = uR < 0f, flipV = vA < 0f;
            // Map the mesh's UV range onto 0..1 of the texture, reversed where the UVs run backwards
            Vector2 scale = new Vector2(flipU ? -1f / rangeU : 1f / rangeU, flipV ? -1f / rangeV : 1f / rangeV);
            Vector2 offset = new Vector2(flipU ? maxU / rangeU : -minU / rangeU, flipV ? maxV / rangeV : -minV / rangeV);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            return $"UV: {(flipU ? "horizontaal gespiegeld, " : "")}{(flipV ? "verticaal gespiegeld, " : "")}bereik {rangeU:0.00} x {rangeV:0.00}.";
        }

        // The XR ray interactors only hit canvases on the layers in their Raycast Mask. In the Starter Assets rig that mask is
        // Default, UI and layer 31 (2147483681). A canvas on any other layer is invisible to the rays: its buttons stop working.
        // Layer 31 is unused, so the briefing canvas lives there. It is excluded from the cameras that draw to the screen.
        private const int BriefingLayerIndex = 31;

        private static int EnsureBriefingLayer()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            bool changed = false;
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty p = layers.GetArrayElementAtIndex(i);
                if (i != BriefingLayerIndex && p.stringValue == BriefingLayerName)
                {
                    p.stringValue = string.Empty;   // an earlier version of this menu put it on a layer the rays cannot hit
                    changed = true;
                }
            }

            SerializedProperty target = layers.GetArrayElementAtIndex(BriefingLayerIndex);
            if (target.stringValue != BriefingLayerName)
            {
                if (!string.IsNullOrEmpty(target.stringValue))
                {
                    throw new System.Exception($"Layer {BriefingLayerIndex} is al in gebruik ('{target.stringValue}').");
                }

                target.stringValue = BriefingLayerName;
                changed = true;
            }

            if (changed)
            {
                tagManager.ApplyModifiedProperties();
            }

            return BriefingLayerIndex;
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing != -1)
            {
                return existing;
            }

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    p.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            throw new System.Exception("Geen vrije layer meer over.");
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            Undo.RecordObject(go, "Briefing layer");
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        // Glyph resolution on the TV, in pixels per metre of TV. A Quest 3 shows roughly 350-600 px per metre at 2.5-4 m
        // from the screen. Text rendered much sharper than that shimmers when you move your head (the glyph atlas has
        // no mipmaps, so the GPU skips texels). Close to the screen's own resolution it stays calm. Raise it for sharper
        // but more shimmering text, lower it for softer and calmer text.
        private const float GlyphPixelsPerMetre = 700f;

        [MenuItem("Tools/Einstein Telescope/Instructiebord/Tekst rustig maken (glyph-resolutie)")]
        public static void SettleText()
        {
            LaserBriefingPanel briefing = FindBriefing();
            if (briefing == null)
            {
                EditorUtility.DisplayDialog("Instructiebord", "Geen LaserBriefingPanel in deze scene.", "OK");
                return;
            }

            var screenObject = new SerializedObject(briefing).FindProperty("screen").objectReferenceValue as GameObject;
            var canvas = screenObject != null ? screenObject.GetComponentInParent<Canvas>(true) : briefing.GetComponentInChildren<Canvas>(true);
            var scaler = canvas != null ? canvas.GetComponent<UnityEngine.UI.CanvasScaler>() : null;
            if (scaler == null)
            {
                EditorUtility.DisplayDialog("Instructiebord", "Geen Canvas met CanvasScaler gevonden bij de briefing.", "OK");
                return;
            }

            float metresPerUnit = canvas.transform.lossyScale.x;   // run 'Briefing op bord zetten' first: that sets this scale
            float value = Mathf.Clamp(GlyphPixelsPerMetre * metresPerUnit, 0.3f, 3f);
            Undo.RecordObject(scaler, "Briefing glyph resolution");
            float before = scaler.dynamicPixelsPerUnit;
            scaler.dynamicPixelsPerUnit = value;
            EditorUtility.SetDirty(scaler);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log($"Instructiebord: Dynamic Pixels Per Unit {before:0.00} -> {value:0.00} ({metresPerUnit * 1000f:0.00} mm per canvas-eenheid, {GlyphPixelsPerMetre:0} px/m).", canvas);
        }

        /// <summary>
        /// Makes the canvas as wide as the TV's aspect ratio asks (height stays). Carlo's briefing uses centre
        /// anchors with fixed sizes, so every element's x position and width is stretched by the same factor:
        /// edges stay on the edges, text keeps its font size and just gets more room. Running it twice is harmless.
        /// </summary>
        private static void MatchAspect(RectTransform canvas, float aspect)
        {
            float oldWidth = canvas.sizeDelta.x;
            float newWidth = canvas.sizeDelta.y * aspect;
            if (oldWidth <= 0f || Mathf.Abs(newWidth - oldWidth) < 0.5f)
            {
                return;
            }

            float k = newWidth / oldWidth;
            canvas.sizeDelta = new Vector2(newWidth, canvas.sizeDelta.y);
            foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == canvas || rect.anchorMin.x != rect.anchorMax.x)   // stretched elements follow by themselves
                {
                    continue;
                }

                Undo.RecordObject(rect, "Fit briefing");
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x * k, rect.anchoredPosition.y);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x * k, rect.sizeDelta.y);
            }
        }

        /// <summary>Centre, outward normal, up and size (m) of the flat screen quad, from its vertices.</summary>
        private static bool ScreenFrame(MeshRenderer screen, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size)
        {
            center = normal = up = Vector3.zero;
            size = Vector2.zero;
            var filter = screen.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount < 3)
            {
                return false;
            }

            Vector3[] v = mesh.vertices;
            Transform t = screen.transform;
            Vector3 a = t.TransformPoint(v[0]), b = t.TransformPoint(v[1]), c = t.TransformPoint(v[2]);
            normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3[] n = mesh.normals;
            if (n != null && n.Length > 0 && Vector3.Dot(normal, t.TransformDirection(n[0])) < 0f)
            {
                normal = -normal;                       // follow the mesh's own normal: out of the wall
            }

            up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            if (up.sqrMagnitude < 0.5f)
            {
                up = Vector3.ProjectOnPlane(t.up, normal).normalized;
            }

            Vector3 right = Vector3.Cross(up, -normal);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            Vector3 sum = Vector3.zero;
            foreach (Vector3 lv in v)
            {
                Vector3 w = t.TransformPoint(lv);
                sum += w;
                minX = Mathf.Min(minX, Vector3.Dot(w, right)); maxX = Mathf.Max(maxX, Vector3.Dot(w, right));
                minY = Mathf.Min(minY, Vector3.Dot(w, up)); maxY = Mathf.Max(maxY, Vector3.Dot(w, up));
            }

            center = sum / v.Length;
            size = new Vector2(maxX - minX, maxY - minY);
            return size.x > 0.01f && size.y > 0.01f;
        }

        private static MeshRenderer FindScreen()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name.StartsWith(ScreenName))
                    {
                        return r;
                    }
                }
            }

            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (r.name.StartsWith(ScreenName))
                {
                    return r;
                }
            }

            return null;
        }

        private static LaserBriefingPanel FindBriefing()
        {
            var all = Object.FindObjectsByType<LaserBriefingPanel>(FindObjectsInactive.Include);
            return all.Length > 0 ? all[0] : null;
        }

        private static Material FindMaterial(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name)
                {
                    return m;
                }
            }

            return null;
        }
    }
}
