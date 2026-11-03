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
