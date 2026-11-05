using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// One alignment level: put it on the root object that holds the level's optics (laser, mirrors,
    /// irises, detector). <see cref="LaserLevelManager"/> switches levels by activating one root at a time.
    /// Checkpoints (usually irises) are judged in order before the final detector, like a technician
    /// who first walks the beam through the irises and then aligns the interferometer.
    /// </summary>
    public class LaserLevel : MonoBehaviour
    {
        [SerializeField] private string title = "Level";

        [Tooltip("Shown on the monitor when the level starts: what the player has to do.")]
        [SerializeField, TextArea] private string briefing = "";

        [Header("Briefing screen")]
        [Tooltip("Briefing page 1: what the player has to do and how the level is won.")]
        [SerializeField, TextArea(4, 20)] private string objectiveText = "";

        [Tooltip("Added under the shared controls on page 2.")]
        [SerializeField, TextArea(2, 8)] private string controlsTip = "";

        [Tooltip("Briefing page 3: what happens to the laser, with simple maths.")]
        [SerializeField, TextArea(4, 20)] private string physicsText = "";

        [Header("Stars")]
        [Tooltip("Finish within this time (seconds) with at most 1 safety incident for 3 stars; within twice this time for 2 stars. " +
                 "0 = use the par time of the LaserAlignmentObjective.")]
        [SerializeField, Min(0f)] private float parTimeSeconds = 0f;

        [Header("Optics")]
        [SerializeField] private LaserEmitter laser = null;

        [Tooltip("The motorised mirrors the player can steer, in the order the panel cycles through them.")]
        [SerializeField] private MirrorMount[] mirrors = new MirrorMount[0];

        [Tooltip("Irises (or other targets) the beam must pass first, in order.")]
        [SerializeField] private LaserTarget[] checkpoints = new LaserTarget[0];

        [Tooltip("Short name per checkpoint for the monitor, e.g. 'Iris A'. Same order as Checkpoints.")]
        [SerializeField] private string[] checkpointLabels = new string[0];

        [Tooltip("What to do when that checkpoint is not passed yet. Same order as Checkpoints.")]
        [SerializeField] private string[] checkpointHints = new string[0];

        [Tooltip("The final detector (shown on the monitor's spot display).")]
        [SerializeField] private LaserTarget detector = null;

        public string Title => title;
        public string Briefing => briefing;
        public string ObjectiveText => objectiveText;
        public string ControlsTip => controlsTip;
        public string PhysicsText => physicsText;

        /// <summary>3-star time limit in seconds; 0 = the objective's default.</summary>
        public float ParTimeSeconds => parTimeSeconds;
        public LaserEmitter Laser => laser;
        public MirrorMount[] Mirrors => mirrors;
        public LaserTarget[] Checkpoints => checkpoints;
        public LaserTarget Detector => detector;

        public string CheckpointLabel(int index)
        {
            if (index >= 0 && index < checkpointLabels.Length && !string.IsNullOrEmpty(checkpointLabels[index]))
            {
                return checkpointLabels[index];
            }

            return index >= 0 && index < checkpoints.Length && checkpoints[index] != null ? checkpoints[index].name : "Checkpoint";
        }

        public string CheckpointHint(int index)
        {
            return index >= 0 && index < checkpointHints.Length ? checkpointHints[index] : string.Empty;
        }

        /// <summary>
        /// Checkpoints followed by the detector: everything that must be aligned at the same time.
        /// Empty slots are left out (an empty slot can never be aligned, so the level could never be won).
        /// </summary>
        public LaserTarget[] AllTargets()
        {
            var all = new System.Collections.Generic.List<LaserTarget>(checkpoints.Length + 1);
            foreach (LaserTarget checkpoint in checkpoints)
            {
                if (checkpoint != null)
                {
                    all.Add(checkpoint);
                }
            }

            if (detector != null)
            {
                all.Add(detector);
            }

            return all.ToArray();
        }
    }
}
