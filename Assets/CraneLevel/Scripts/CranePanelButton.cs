using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ETVRTool.CraneLevel
{
    public enum PanelAction { None, AttachRelease, EmergencyStop, ResetStop, ToggleCreep, RestartLevel }

    /// <summary>
    /// A push button on the crane control cabinet. Press it with your finger (poke) or
    /// point at it and hold grip/trigger (ray).
    ///  - Hold buttons move the crane while they are held down.
    ///  - Momentary buttons do one action per press (attach, e-stop, ...).
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class CranePanelButton : MonoBehaviour
    {
        public enum Mode { HoldAxis, Momentary }

        public Mode mode = Mode.HoldAxis;
        public BridgeCrane crane;
        public CraneHook hook;
        public PipeLevelManager level;

        [Header("Hold mode")]
        public CraneAxis axis;
        [Range(-1f, 1f)] public float direction = 1f;

        [Header("Momentary mode")]
        public PanelAction action;

        [Header("Visual")]
        [Tooltip("The part that moves in when pressed.")]
        public Transform cap;
        public float pressDepth = 0.01f;

        XRSimpleInteractable _interactable;
        Vector3 _capRest;
        int _pressCount; // several interactors can press at once

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            if (cap != null) _capRest = cap.localPosition;
        }

        void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
            _pressCount = 0;
            Release();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            _pressCount++;
            if (_pressCount == 1) Press();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            _pressCount = Mathf.Max(0, _pressCount - 1);
            if (_pressCount == 0) Release();
        }

        public void Press()
        {
            if (cap != null) cap.localPosition = _capRest - Vector3.forward * pressDepth;

            if (mode == Mode.HoldAxis)
            {
                if (crane != null) crane.SetButtonInput(axis, direction);
                return;
            }

            switch (action)
            {
                case PanelAction.AttachRelease: if (hook != null) hook.ToggleAttach(); break;
                case PanelAction.EmergencyStop: if (crane != null) crane.EmergencyStop(); break;
                case PanelAction.ResetStop: if (crane != null) crane.ResetEmergencyStop(); break;
                case PanelAction.ToggleCreep: if (crane != null) crane.ToggleCreep(); break;
                case PanelAction.RestartLevel: if (level != null) level.RestartLevel(); break;
            }
        }

        public void Release()
        {
            if (cap != null) cap.localPosition = _capRest;
            if (mode == Mode.HoldAxis && crane != null) crane.SetButtonInput(axis, 0f);
        }
    }
}
