namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Texts of the level briefing on the instruction board TV (<see cref="LaserBriefingPanel"/>): one shared
    /// controls page and, per level, an objective page and a short controls tip. This is the single source of
    /// truth, so the texts can change without editing the scene. Levels are matched by their title
    /// ("Michelson", "Beam walking", "Long arms"), with the level number as a fallback.
    /// Written for secondary-school students in VR: short sentences, bullets. Rich text: &lt;b&gt;bold&lt;/b&gt;.
    /// Only symbols the legacy built-in font has (• ▲ ▼ ◄ ► °).
    /// The stars section is added at runtime by the panel (from <see cref="LaserAlignmentObjective"/>).
    /// </summary>
    public static class LaserBriefingTexts
    {
        // ----- Controls page (same for every level) ---------------------------------------------------------

        public const string Controls =
            "<b>Buttons</b>   Aim your controller at a button on the curved monitor and pull the <b>trigger</b>.\n" +
            "•  <b>PREV / NEXT</b>: choose a mirror. It gets a glowing ring and the beam coming off it turns <b>cyan</b>.\n" +
            "•  <b>▲ ▼ ◄ ►</b>: tilt that mirror; the dot moves the way the arrow points. Tap = one step, hold = keep moving.\n" +
            "•  Centre button <b>COARSE / FINE</b>: big steps to find the target, small steps for the last bit.\n" +
            "•  <b>UNDO</b>: take back your last move.   <b>RESET</b>: mirror back to its start.\n" +
            "•  <b>HELP</b>: open this screen again.   <b>NEXT LEVEL</b>: skip to the next level.\n" +
            "•  Or hold <b>GRIP</b> and push the thumbstick to tilt.  A / X = coarse / fine,  B / Y = next mirror.\n\n" +
            "<b>Mirror camera</b>   Shows the beam of the mirror you steer. <b>VIEW</b> switches the picture:\n" +
            "•  WHOLE PATH = seen from above.  CLOSE-UP = along your beam, zoomed in.  OVERVIEW / TOP = the mirror itself.\n" +
            "•  <b>Cyan line</b> = your beam.  <b>Ring</b> = TARGET (the next iris or the detector).  <b>Dot</b> = BEAM LANDS HERE.\n" +
            "•  The <b>arrow</b> points from the dot to the ring: move the beam that way. Green ring = on target.\n" +
            "•  In CLOSE-UP the banner tells you which buttons to press, for example  \"Press ► 3x and ▲ 2x\".\n" +
            "•  The small circles at the bottom of CLOSE-UP are the mirrors on the way: keep their dot inside. Amber = near the edge.\n" +
            "•  NO LASER REACHES THIS MIRROR: press PREV and steer the mirror before it first.\n\n" +
            "<b>Detector screen</b>   Where the beams land: orange = not yet, green = centred. Four equal bars = centred.\n" +
            "<b>Beam cameras</b>   Close-ups of the irises. The text on the monitor always says what to do next.";

        // ----- Level 1: Michelson -----------------------------------------------------------------------------

        public const string ObjectiveMichelson =
            "The <b>beam splitter</b> cuts the laser beam in two. Each half runs 6.4 m down an arm to an <b>end mirror</b> " +
            "in a middle tower, bounces back and meets the other half at the <b>output detector</b>. That is the spot where " +
            "a real detector would notice a passing gravitational wave.\n\n" +
            "<b>Your task</b>\n" +
            "•  Steer <b>End mirror X</b> and <b>End mirror Y</b> until both returning beams land in the centre of the output detector.\n" +
            "•  Keep both spots there for 2 seconds.\n" +
            "•  Tip: bring one beam onto the detector first, then the other.";

        public const string TipMichelson =
            "Only End mirror X and End mirror Y can be steered. Use COARSE to get a beam onto the detector, then switch to FINE.";

        // ----- Level 2: Beam walking --------------------------------------------------------------------------

        public const string ObjectiveBeamWalking =
            "In a real detector the laser does not point straight at the beam splitter. Two <b>steering mirrors</b> in the " +
            "injection tower guide it there, and two <b>irises</b> (small round holes) show whether the beam follows the right line.\n\n" +
            "<b>Your task</b>\n" +
            "1.  Send the beam through the centre of <b>Iris A</b> and <b>Iris B</b>, on the way to the beam splitter.\n" +
            "2.  Then align <b>End mirror X</b> and <b>End mirror Y</b> on the output detector, like in level 1.\n" +
            "3.  Everything must be aligned at the same time for 2 seconds.\n\n" +
            "<b>Beam walking</b>   Steering mirror 1 centres the beam on Iris A, Steering mirror 2 on Iris B. Fixing Iris B " +
            "moves the beam a little on Iris A, so go back and forth: every round the error gets smaller.";

        public const string TipBeamWalking =
            "PREV / NEXT cycles through 4 mirrors: Steering 1, Steering 2, End X, End Y. Finish Iris A and B before you touch the end mirrors.";

        // ----- Level 3: Long arms -----------------------------------------------------------------------------

        public const string ObjectiveLongArms =
            "Now the end mirrors sit in the far <b>end towers</b>, almost 16 m from the beam splitter. A longer arm makes the " +
            "detector more sensitive, but harder to align. The Einstein Telescope will have arms of 10 km!\n\n" +
            "<b>Your task</b>\n" +
            "1.  Walk the beam through <b>Iris A</b> and <b>Iris B</b> with Steering mirror 1 and 2.\n" +
            "2.  Check <b>Iris X</b> and <b>Iris Y</b> in the middle towers. No mirror of their own steers them: if the beam " +
            "misses one, the incoming beam is still crooked, so go back to step 1.\n" +
            "3.  Align <b>End mirror X</b> and <b>End mirror Y</b> on the output detector.\n" +
            "All four irises and the detector must be aligned at the same time for 2 seconds.";

        public const string TipLongArms =
            "4 mirrors: Steering 1, Steering 2, End X, End Y. Work from the laser outwards: a steering mirror moves the beam " +
            "on everything after it, so check the irises again after you touch one.";

        // ----- Shared lines on the objective page -------------------------------------------------------------

        public const string StarsHeader = "<b>Stars</b>";

        public const string SafetyLine =
            "A <b>safety incident</b> is the laser hitting something it should not: a wall, a frame or the edge of an optic. " +
            "When the level is done, your result appears on the monitor.";

        // ----- Lookup -----------------------------------------------------------------------------------------

        /// <summary>
        /// Which built-in level this is: 0 Michelson, 1 Beam walking, 2 Long arms, -1 unknown.
        /// Matched by title first; an unknown title falls back to the level number (0-based).
        /// </summary>
        public static int LevelKey(LaserLevel level, int index)
        {
            string title = level != null && level.Title != null ? level.Title.Trim().ToLowerInvariant() : string.Empty;
            if (title.Contains("michelson"))
            {
                return 0;
            }

            if (title.Contains("beam walking"))
            {
                return 1;
            }

            if (title.Contains("long arms"))
            {
                return 2;
            }

            return index >= 0 && index <= 2 ? index : -1;
        }

        /// <summary>Objective page text of a level, without the stars section. Null when the level is unknown.</summary>
        public static string Objective(LaserLevel level, int index)
        {
            switch (LevelKey(level, index))
            {
                case 0: return ObjectiveMichelson;
                case 1: return ObjectiveBeamWalking;
                case 2: return ObjectiveLongArms;
                default: return null;
            }
        }

        /// <summary>Tip under the controls for a level. Null when the level is unknown.</summary>
        public static string ControlsTip(LaserLevel level, int index)
        {
            switch (LevelKey(level, index))
            {
                case 0: return TipMichelson;
                case 1: return TipBeamWalking;
                case 2: return TipLongArms;
                default: return null;
            }
        }
    }
}
