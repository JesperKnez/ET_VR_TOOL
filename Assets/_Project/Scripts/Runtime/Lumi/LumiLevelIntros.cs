using EinsteinTelescope.Laser;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// What Lumi says out loud the first time a level starts: a short version of the level briefing on the TV
    /// (LaserBriefingTexts holds the full text, so keep the two in step). Scripted, so no AI is involved.
    /// Written to be spoken: where you are and what your job is, in plain words, at most about 15 seconds per level.
    /// </summary>
    public static class LumiLevelIntros
    {
        public const string Michelson =
            "Hi, I'm Lumi! This is ETpathfinder, a real research lab. Your job is to steer the two end mirrors " +
            "until both beams hit the centre of the detector. To press the monitor's buttons, aim and pull the trigger.";

        public const string BeamWalking =
            "This level is called beam walking. Your job is to use the steering mirrors to send the beam through " +
            "both irises, the small round holes. Then line up the end mirrors again.";

        public const string LongArms =
            "This level is called long arms, because the end mirrors are much farther away! Your job is to get the " +
            "beam through the irises first, then line up the end mirrors.";

        /// <summary>The line for a level, matched the same way as the TV briefing. Null for an unknown level.</summary>
        public static string For(LaserLevel level, int index)
        {
            switch (LaserBriefingTexts.LevelKey(level, index))
            {
                case 0: return Michelson;
                case 1: return BeamWalking;
                case 2: return LongArms;
                default: return null;
            }
        }
    }
}
