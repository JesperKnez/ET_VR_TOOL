using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>Lumi's ongoing mood: which looping clip the Animator's "Mood" layer plays.</summary>
    public enum LumiMode
    {
        Idle = 0,   // Idle_Hover
        Listen = 1, // Listen
        Think = 2,  // Think (e.g. while waiting for the AI's answer)
        Talk = 3,   // Talk_Gesture
    }

    /// <summary>
    /// Names (and pre-hashed ids) of the parameters in Lumi's Animator Controller, so every script
    /// spells them the same way. The controller is built by Tools > Einstein Telescope > Lumi.
    ///
    /// Mood layer:     Mode (int, a <see cref="LumiMode"/>) cross-fades between the looping clips.
    /// Gestures layer: Wave, Celebrate, Puzzled, FocusZoom are triggers (play once, then back to the mood).
    ///                 Pointing and Sad are bools (hold the pose while true).
    /// </summary>
    public static class LumiAnimatorParams
    {
        public const string Mode = "Mode";
        public const string Wave = "Wave";
        public const string Celebrate = "Celebrate";
        public const string Puzzled = "Puzzled";
        public const string FocusZoom = "FocusZoom";
        public const string Pointing = "Pointing";
        public const string Sad = "Sad";

        public static readonly int ModeId = Animator.StringToHash(Mode);
        public static readonly int WaveId = Animator.StringToHash(Wave);
        public static readonly int CelebrateId = Animator.StringToHash(Celebrate);
        public static readonly int PuzzledId = Animator.StringToHash(Puzzled);
        public static readonly int FocusZoomId = Animator.StringToHash(FocusZoom);
        public static readonly int PointingId = Animator.StringToHash(Pointing);
        public static readonly int SadId = Animator.StringToHash(Sad);
    }
}
