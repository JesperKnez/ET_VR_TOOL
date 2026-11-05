using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EinsteinTelescope.Lumi.Editor
{
    /// <summary>
    /// Menu: Tools > Einstein Telescope > Lumi.
    /// Builds Lumi's Animator Controller from the clips inside Lumi.fbx, and assigns it to Lumi.
    ///
    /// Layer "Mood" (base): the four loops, picked by the int parameter Mode (see LumiMode).
    ///   Any State -> loop when Mode matches, with a short cross-fade.
    /// Layer "Gestures" (override): an empty "None" state plus the one-shots.
    ///   Triggers Wave / Celebrate / Puzzled / FocusZoom play once and return to None.
    ///   Bools Pointing / Sad hold their pose while true.
    /// Write Defaults is OFF everywhere (safe, because the FBX export bakes every bone into every clip).
    /// A layer with Write Defaults off keeps showing its last pose even in an empty state, so the
    /// Gestures layer's WEIGHT does the blending instead: every gesture state fades it to 1, and
    /// "None" fades it back to 0 (LumiLayerWeightFade), so the Mood layer shows through between gestures.
    /// </summary>
    public static class LumiAnimatorMenu
    {
        private const string FbxPath = "Assets/_Project/Models/Lumi/Lumi.fbx";
        private const string Folder = "Assets/_Project/Animations/Lumi";
        private const string ControllerPath = Folder + "/Lumi.controller";

        private const float MoodFade = 0.3f;    // seconds: cross-fade between loops
        private const float GestureIn = 0.15f;  // seconds: blend into a gesture
        private const float GestureOut = 0.25f; // seconds: blend back to the mood

        private static readonly (LumiMode mode, string clip)[] Moods =
        {
            (LumiMode.Idle, "Idle_Hover"),
            (LumiMode.Listen, "Listen"),
            (LumiMode.Think, "Think"),
            (LumiMode.Talk, "Talk_Gesture"),
        };

        private static readonly (string trigger, string clip)[] TriggerGestures =
        {
            (LumiAnimatorParams.Wave, "Wave"),
            (LumiAnimatorParams.Celebrate, "Celebrate"),
            (LumiAnimatorParams.Puzzled, "Puzzled"),
            (LumiAnimatorParams.FocusZoom, "Focus_Zoom"),
        };

        private static readonly (string boolean, string clip)[] HoldGestures =
        {
            (LumiAnimatorParams.Pointing, "Point"),
            (LumiAnimatorParams.Sad, "Sad"),
        };

        [MenuItem("Tools/Einstein Telescope/Lumi/Build Animator Controller", priority = 0)]
        public static void BuildController()
        {
            Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToDictionary(c => c.name);

            string[] needed = Moods.Select(m => m.clip)
                .Concat(TriggerGestures.Select(g => g.clip))
                .Concat(HoldGestures.Select(g => g.clip)).ToArray();
            string[] missing = needed.Where(n => !clips.ContainsKey(n)).ToArray();
            if (missing.Length > 0)
            {
                EditorUtility.DisplayDialog("Lumi Animator",
                    "These clips are missing in " + FbxPath + ":\n" + string.Join(", ", missing) +
                    "\n\nCheck the FBX's Animation tab: Import Animation on, clips renamed to these names.", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                if (!EditorUtility.DisplayDialog("Lumi Animator",
                        ControllerPath + " already exists. Rebuild it? Manual changes to it will be lost.",
                        "Rebuild", "Cancel"))
                {
                    return;
                }
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            EnsureFolder(Folder);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter(LumiAnimatorParams.Mode, AnimatorControllerParameterType.Int);
            foreach (var (trigger, _) in TriggerGestures)
            {
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
            }
            foreach (var (boolean, _) in HoldGestures)
            {
                controller.AddParameter(boolean, AnimatorControllerParameterType.Bool);
            }

            BuildMoodLayer(controller, clips);
            BuildGestureLayer(controller, clips);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            ReassignInOpenScenes(controller);
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
            Debug.Log("[Lumi] Animator Controller built: " + ControllerPath);
        }

        [MenuItem("Tools/Einstein Telescope/Lumi/Assign Animator Controller To Selected Lumi", priority = 1)]
        public static void AssignToSelection()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            GameObject selected = Selection.activeGameObject;
            Animator animator = selected != null ? selected.GetComponentInChildren<Animator>() : null;
            if (controller == null || animator == null)
            {
                EditorUtility.DisplayDialog("Lumi Animator",
                    controller == null
                        ? "Build the Animator Controller first (Tools > Einstein Telescope > Lumi > Build Animator Controller)."
                        : "Select Lumi in the Hierarchy first (the object with the Animator).", "OK");
                return;
            }

            Undo.RecordObject(animator, "Assign Lumi Animator Controller");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // Lumi is moved by scripts, not by its clips
            EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);
            Debug.Log("[Lumi] Assigned " + ControllerPath + " to " + animator.name);
        }

        /// <summary>Rebuilding creates a new controller asset, so re-link every Lumi in the open scenes.</summary>
        private static void ReassignInOpenScenes(AnimatorController controller)
        {
            foreach (Animator animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include))
            {
                if (animator.avatar == null || AssetDatabase.GetAssetPath(animator.avatar) != FbxPath) continue;
                Undo.RecordObject(animator, "Assign Lumi Animator Controller");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);
                Debug.Log("[Lumi] Re-assigned the controller to " + animator.name + " in " + animator.gameObject.scene.name +
                          " (save the scene).");
            }
        }

        private static void BuildMoodLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = "Mood";
            controller.layers = layers;
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            for (int i = 0; i < Moods.Length; i++)
            {
                (LumiMode mode, string clipName) = Moods[i];
                AnimatorState state = machine.AddState(clipName, new Vector3(300, 60 + i * 70, 0));
                state.motion = clips[clipName];
                state.writeDefaultValues = false;
                if (mode == LumiMode.Idle)
                {
                    machine.defaultState = state;
                }

                AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.Equals, (int)mode, LumiAnimatorParams.Mode);
                enter.hasExitTime = false;
                enter.hasFixedDuration = true;
                enter.duration = MoodFade;
                enter.canTransitionToSelf = false;
            }
        }

        private static void BuildGestureLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            controller.AddLayer("Gestures");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[1].defaultWeight = 0f; // faded in by each gesture state (LumiLayerWeightFade)
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;
            AnimatorStateMachine machine = controller.layers[1].stateMachine;

            AnimatorState none = machine.AddState("None", new Vector3(300, 0, 0));
            none.writeDefaultValues = false;
            AddWeightFade(none, 0f, GestureOut); // let the mood show through
            machine.defaultState = none;

            int row = 1;
            foreach (var (trigger, clipName) in TriggerGestures)
            {
                AnimatorState state = AddGestureState(machine, clips[clipName], row++);
                AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
                SetFade(enter, GestureIn, hasExitTime: false);
                enter.canTransitionToSelf = false;

                AnimatorStateTransition back = state.AddTransition(none);
                SetFade(back, 0f, hasExitTime: true); // when the clip has played once; the weight fade blends
            }

            foreach (var (boolean, clipName) in HoldGestures)
            {
                AnimatorState state = AddGestureState(machine, clips[clipName], row++);
                AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, boolean);
                SetFade(enter, GestureIn, hasExitTime: false);
                enter.canTransitionToSelf = false;

                AnimatorStateTransition back = state.AddTransition(none);
                back.AddCondition(AnimatorConditionMode.IfNot, 0, boolean);
                SetFade(back, 0f, hasExitTime: false); // the weight fade blends back to the mood
            }
        }

        private static AnimatorState AddGestureState(AnimatorStateMachine machine, AnimationClip clip, int row)
        {
            AnimatorState state = machine.AddState(clip.name, new Vector3(560, row * 60, 0));
            state.motion = clip;
            state.writeDefaultValues = false;
            AddWeightFade(state, 1f, GestureIn); // fade the gesture in
            return state;
        }

        private static void AddWeightFade(AnimatorState state, float weight, float seconds)
        {
            var fade = state.AddStateMachineBehaviour<LumiLayerWeightFade>();
            fade.Configure(weight, seconds);
            EditorUtility.SetDirty(fade); // make sure the configured values are saved into the controller
        }

        private static void SetFade(AnimatorStateTransition transition, float seconds, bool hasExitTime)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = seconds;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
