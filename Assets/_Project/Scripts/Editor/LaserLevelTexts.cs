namespace EinsteinTelescope.Laser.Editor
{
    /// <summary>
    /// Texts of the floating briefing screen. The demo builder copies them into each LaserLevel and the
    /// LaserBriefingPanel, where they can be edited in the inspector. Numbers match the demo layout.
    /// Rich text: &lt;b&gt;bold&lt;/b&gt;. Kept to symbols the built-in font has (θ Δ ≈ · ° •).
    /// </summary>
    internal static class LaserLevelTexts
    {
        public const string Controls =
            "<b>Control pad</b>  (point a controller ray at a button and pull the trigger, or click with the mouse)\n" +
            "•  PREV / NEXT: choose the mirror to steer. It gets a glowing ring, and the light coming off it turns cyan.\n" +
            "•  \u25B2 \u25BC tilt it up / down,  \u25C4 \u25BA tilt it left / right. Tap = one step, hold = keep moving.\n" +
            "•  Centre button: COARSE (0.5° per step) or FINE (0.05°). Use FINE for the last centimetre.\n" +
            "•  RESET MIRROR: start angle.   SHUTTER: beam on / off.   LASER: green 532 nm or invisible infrared 1064 nm.\n" +
            "•  UNDO: take back your last move.   IR VIEWER: shows infrared light.   NEXT LEVEL: skip ahead.   HELP: this screen.\n" +
            "<b>Thumbstick:</b> hold GRIP and push the stick to tilt the selected mirror. A / X = fine, B / Y = next mirror.\n\n" +
            "<b>Monitor:</b> the circle shows where the beams hit the output detector (goal: within 1 cm of the centre). Four equal bars = centred, " +
            "the text says what to do next. Above it: close-ups of the irises. Left of the pad: the view from the selected mirror.\n" +
            "<b>Sound:</b> beeps get higher and faster as the beam nears the centre of the next target.\n" +
            "<b>Keyboard:</b> arrows tilt,  ,  .  mirror,  /  fine,  U undo,  Backspace reset,  L shutter,  K laser,  I IR,  N next level,  H help,  T beeps.";

        // ----- Level 1 -----------------------------------------------------------------------------

        public const string ObjectiveMichelson =
            "The laser beam is split in two by the <b>beam splitter</b>. Each half travels down an arm, bounces off an " +
            "<b>end test mass</b> (a 62 cm mirror, the size planned for the Einstein Telescope) and comes back. Where the two " +
            "returning beams meet is the <b>output port</b>: in a real detector, that is where a passing gravitational wave shows up.\n\n" +
            "<b>Your task</b>\n" +
            "Steer End mirror X and End mirror Y until both returning beams land inside the green circle of the output " +
            "detector (within 1 cm) and keep them there for 2 seconds.\n\n" +
            "<b>Stars</b>\n" +
            "3 stars: within 2 minutes and at most 1 safety incident.   2 stars: within 4 minutes and at most 4.\n" +
            "A safety incident is laser light hitting something it should not: a wall, a frame or the edge of an optic.";

        public const string TipMichelson =
            "only the two end mirrors can be steered. Use COARSE steps to bring a beam onto the detector, then switch to FINE.";

        public const string PhysicsMichelson =
            "<b>1. Law of reflection</b>\n" +
            "A beam leaves a mirror at the same angle as it arrives:  angle in = angle out.\n\n" +
            "<b>2. Tilt a mirror by θ and the beam turns by 2θ</b>\n" +
            "Both angles change, so the turn is doubled. After a distance L the spot moves:\n" +
            "            Δx ≈ 2 · θ · L          (θ in radians:  1° ≈ 0.0175 rad)\n" +
            "End mirror X is L ≈ 3.7 m from the detector (2.5 m back to the splitter + 1.2 m to the detector). One COARSE step " +
            "of 0.5° = 0.0087 rad moves the spot 2 × 0.0087 × 3.7 ≈ 6.5 cm. One FINE step moves it 6.5 mm. The goal is 1 cm, " +
            "so the last part needs FINE steps.\n\n" +
            "<b>3. The beam splitter</b>\n" +
            "It reflects half of the light and lets half through (50 / 50). The glass bends the light that goes through " +
            "(Snell's law:  n1 · sin θ1 = n2 · sin θ2) and shifts it sideways by about 2.4 cm. That is why the detector is not " +
            "exactly in the middle.\n\n" +
            "<b>4. Why both beams must overlap</b>\n" +
            "In a real detector the two returning beams interfere. A gravitational wave changes the arm lengths by less than " +
            "the width of a proton; only perfectly overlapping beams can show that.";

        // ----- Level 2 -----------------------------------------------------------------------------

        public const string ObjectiveBeamWalking =
            "In a real detector the laser does not point straight at the beam splitter. It is guided by <b>steering mirrors</b>, " +
            "and technicians use small round holes called <b>irises</b> to check that the beam follows the right line.\n\n" +
            "<b>Your task</b>\n" +
            "1.  Use Steering mirror 1 and 2 to send the beam through the centre of Iris A and Iris B (within 1 cm).\n" +
            "2.  Then align End mirror X and End mirror Y on the output detector, like in level 1.\n" +
            "Everything must be aligned at the same time for 2 seconds. The monitor always names the first iris the beam " +
            "does not pass yet.\n\n" +
            "<b>Stars</b>\n" +
            "Same rules as level 1: within 2 minutes and at most 1 safety incident for 3 stars.";

        public const string TipBeamWalking =
            "PREV / NEXT cycles through 4 mirrors: Steering 1, Steering 2, End X, End Y. " +
            "Beam walking: Steering mirror 1 centres the beam on Iris A, Steering mirror 2 on Iris B; repeat.";

        public const string PhysicsBeamWalking =
            "<b>1. A beam line has a position and a direction</b>\n" +
            "To put a beam on a fixed line you must set both, so you need two mirrors. That is why there are two steering " +
            "mirrors and two irises.\n\n" +
            "<b>2. Each mirror moves the spot on both irises</b>\n" +
            "            Δx ≈ 2 · θ · L          (L = distance from the mirror to the iris)\n" +
            "•  Steering mirror 1 to Iris A:  L ≈ 1.9 m, one COARSE step moves the spot about 3.3 cm.\n" +
            "•  Steering mirror 2 to Iris B:  L ≈ 1.45 m, one COARSE step moves it about 2.5 cm.\n" +
            "FINE steps are ten times smaller.\n\n" +
            "<b>3. Beam walking</b>\n" +
            "Centring Iris B with mirror 2 shifts the beam a little at Iris A, so you alternate: mirror 1 for Iris A, " +
            "mirror 2 for Iris B, and repeat. Each round the errors get smaller, like solving two equations step by step.\n\n" +
            "<b>4. The light hits these mirrors at 45°</b>\n" +
            "A left / right tilt still turns the beam by 2θ, but an up / down tilt turns it by only about 1.4θ " +
            "(2θ · cos 45°).";

        // ----- Level 3 -----------------------------------------------------------------------------

        public const string ObjectiveFoldedArms =
            "Now each arm is <b>folded</b> by an extra mirror, like the GEO600 detector in Germany, which folds its 600 m arms " +
            "so that the light travels 1200 m.\n\n" +
            "<b>Your task</b>\n" +
            "1.  Walk the beam through Iris A and Iris B with the two steering mirrors.\n" +
            "2.  Use Fold mirror X to centre the beam on Iris X, and Fold mirror Y for Iris Y.\n" +
            "3.  Align End mirror X and End mirror Y on the output detector.\n" +
            "All four irises and the detector must be aligned at the same time for 2 seconds.\n\n" +
            "<b>Stars</b>\n" +
            "Same rules, but with 6 mirrors: work calmly from the laser outwards.";

        public const string TipFoldedArms =
            "PREV / NEXT cycles through 6 mirrors: Steering 1, Steering 2, Fold X, End X, Fold Y, End Y. " +
            "Work in that order: a mirror early in the path moves everything after it.";

        public const string PhysicsFoldedArms =
            "<b>1. Errors add up</b>\n" +
            "Every mirror the beam meets turns it by 2θ. The spot error on the detector is the sum of all the mirror errors " +
            "before it, each multiplied by its own distance L:\n" +
            "            Δx ≈ 2 · (θ1 · L1 + θ2 · L2 + θ3 · L3 + ...)\n\n" +
            "<b>2. Longer paths make it harder</b>\n" +
            "End mirror X is now about 5.6 m from the detector (2.6 m to the fold mirror, 1.8 m to the splitter, 1.2 m to the " +
            "detector). One COARSE step moves the spot 2 × 0.0087 × 5.6 ≈ 9.8 cm, one FINE step about 1 cm: exactly the size " +
            "of the goal.\n\n" +
            "<b>3. Work from the laser outwards</b>\n" +
            "A mirror early in the path moves the beam on every iris and mirror after it. Fix the steering mirrors first, then " +
            "the fold mirrors, then the end mirrors. If you change an early mirror later, check the irises after it again.\n\n" +
            "<b>4. Why fold an arm?</b>\n" +
            "A longer light path makes the tiny length change caused by a gravitational wave easier to measure. The Einstein " +
            "Telescope is planned with 10 km arms, deep underground.";
    }
}
