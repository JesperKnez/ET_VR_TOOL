using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Fades the weight of the Animator layer it sits on towards a target while its state is active.
    ///
    /// Why: Lumi's "Gestures" layer overrides the "Mood" layer. With Write Defaults off, a layer keeps
    /// showing the last pose it played even after it returns to its empty "None" state, which would
    /// hide every mood change after the first gesture. So each gesture state fades the layer in
    /// (weight → 1) and the "None" state fades it out (weight → 0): the mood shows through again, and
    /// the fade-out blends smoothly from the gesture's last pose back into the mood.
    /// Added and configured by Tools > Einstein Telescope > Lumi > Build Animator Controller.
    /// </summary>
    public class LumiLayerWeightFade : StateMachineBehaviour
    {
        [Tooltip("Layer weight to move towards while this state is active.")]
        [SerializeField, Range(0f, 1f)] private float targetWeight = 1f;
        [Tooltip("Seconds for a full 0 → 1 (or 1 → 0) fade.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.2f;

        /// <summary>Used by the editor tool that builds the controller.</summary>
        public void Configure(float weight, float seconds)
        {
            targetWeight = weight;
            fadeSeconds = seconds;
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            float current = animator.GetLayerWeight(layerIndex);
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            animator.SetLayerWeight(layerIndex, Mathf.MoveTowards(current, targetWeight, step));
        }
    }
}
