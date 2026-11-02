using EinsteinTelescope.Laser;
using EinsteinTelescope.Props;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Props.Editor
{
    /// <summary>
    /// Menu: Tools > Einstein Telescope > Instructiebord > Briefing op bord zetten.
    /// Puts the existing laser briefing (LaserBriefingPanel, "Briefing Screen (floating)") exactly on the
    /// screen of the instruction board and wires the board's standby screen. Run it again after moving the
    /// board: it re-fits everything. Undo (Ctrl+Z) reverts it.
    /// </summary>
    public static class InstructionBoardSetup
    {
        private const string ScreenName = "InstructionBoard_Screen";
        private const string StandbyCanvasName = "Board Standby Canvas";
        private const float Fill = 0.96f;          // share of the screen height the briefing uses
        private const float SurfaceOffset = 0.004f; // canvas in front of the glass (m), avoids z-fighting

        [MenuItem("Tools/Einstein Telescope/Instructiebord/Briefing op bord zetten")]
        public static void FitBriefingToBoard()
        {
            MeshRenderer screen = FindScreen();
            if (screen == null)
            {
                EditorUtility.DisplayDialog("Instructiebord",
                    "Geen '" + ScreenName + "' gevonden in de scene.\nSleep ET_InstructieBord.fbx in de scene (of selecteer het bord) en probeer opnieuw.", "OK");
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

            Undo.SetCurrentGroupName("Briefing op instructiebord");
            int group = Undo.GetCurrentGroup();

            // 1. Briefing root onto the screen. Canvas front faces -Z, so its forward points into the wall.
            Transform root = briefing.transform;
            Undo.RecordObject(root, "Move briefing");
            root.SetPositionAndRotation(center + normal * SurfaceOffset, Quaternion.LookRotation(-normal, up));

            var so = new SerializedObject(briefing);
            var screenObject = so.FindProperty("screen").objectReferenceValue as GameObject;
            if (screenObject != null && screenObject.transform is RectTransform canvasRect)
            {
                Undo.RecordObject(canvasRect, "Fit briefing");
                float scale = Mathf.Min(size.x * Fill / canvasRect.sizeDelta.x, size.y * Fill / canvasRect.sizeDelta.y);
                float parentScale = Mathf.Max(1e-6f, root.lossyScale.x);
                canvasRect.localPosition = Vector3.zero;
                canvasRect.localRotation = Quaternion.identity;
                canvasRect.localScale = Vector3.one * (scale / parentScale);
            }

            // 2. Board component + standby canvas (invisible full-screen button).
            Transform boardRoot = screen.transform.parent != null ? screen.transform.parent : screen.transform;
            var board = boardRoot.GetComponent<InstructionBoard>();
            if (board == null)
            {
                board = Undo.AddComponent<InstructionBoard>(boardRoot.gameObject);
            }

            GameObject standby = BuildStandbyCanvas(boardRoot, center, normal, up, size, board);

            var boardSo = new SerializedObject(board);
            boardSo.FindProperty("briefing").objectReferenceValue = briefing;
            boardSo.FindProperty("screenRenderer").objectReferenceValue = screen;
            boardSo.FindProperty("standbyMaterial").objectReferenceValue = FindMaterial("ET2_BoardScreen");
            boardSo.FindProperty("activeMaterial").objectReferenceValue = FindMaterial("ET2_BoardScreen_Active");
            boardSo.FindProperty("standbyCanvas").objectReferenceValue = standby;
            boardSo.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            Selection.activeGameObject = boardRoot.gameObject;
            Debug.Log($"Instructiebord: briefing op het scherm gezet ({size.x:0.00} x {size.y:0.00} m).", boardRoot);
        }

        private static GameObject BuildStandbyCanvas(Transform boardRoot, Vector3 center, Vector3 normal, Vector3 up, Vector2 size, InstructionBoard board)
        {
            Transform old = boardRoot.Find(StandbyCanvasName);
            if (old != null)
            {
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            var go = new GameObject(StandbyCanvasName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Standby canvas");
            go.transform.SetParent(boardRoot, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.SetPositionAndRotation(center + normal * (SurfaceOffset * 0.5f), Quaternion.LookRotation(-normal, up));
            float parentScale = Mathf.Max(1e-6f, boardRoot.lossyScale.x);
            rect.localScale = new Vector3(size.x / 1920f, size.y / 1080f, 1f) / parentScale;

            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            AddUiRaycaster(go);

            var hit = new GameObject("Open Briefing (invisible button)", typeof(RectTransform));
            hit.transform.SetParent(rect, false);
            var hitRect = (RectTransform)hit.transform;
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = hitRect.offsetMax = Vector2.zero;
            var image = hit.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);    // invisible, still catches the controller ray
            var button = hit.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityEventTools.AddPersistentListener(button.onClick, board.OpenBriefing);
            return go;
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

        /// <summary>XR controller rays need the XRI Tracked Device Graphic Raycaster (added by name, like Carlo's kit).</summary>
        private static void AddUiRaycaster(GameObject canvasObject)
        {
            const string trackedRaycaster = "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster";
            // TypeCache instead of AppDomain.GetAssemblies(): the latter can return unloaded assemblies (warning UAC0005)
            foreach (System.Type type in TypeCache.GetTypesDerivedFrom<UnityEngine.EventSystems.BaseRaycaster>())
            {
                if (type.FullName == trackedRaycaster)
                {
                    canvasObject.AddComponent(type);
                    return;
                }
            }

            canvasObject.AddComponent<GraphicRaycaster>();
        }
    }
}
