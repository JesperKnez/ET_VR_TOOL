using System;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// The short lines Lumi says by itself during a level: warnings, wins and hints when the player is stuck.
    /// Scripted, so no AI is involved and they also work offline. The timer is running when most of these
    /// are spoken, so keep each one to a sentence or two. Lumi is curious, never disappointed.
    /// </summary>
    public static class LumiReactionLines
    {
        public const string HintOpening = "Need a hint? ";

        public const string LaserOff = "The laser is off for a moment. It switches back on by itself.";
        public const string NoLaserOnMirror = "No laser reaches this mirror yet. Press PREV and steer the mirror before it first.";
        public const string NoLightOnDetector = "No light reaches the detector yet. Steer the end mirrors until the beams get there.";
        public const string MissingBeam = "One beam is there, but not the other. Press NEXT and bring the second beam to the detector too.";
        public const string NotCentred = "You're close! Switch to FINE steps and move the spots to the centre.";

        /// <summary>A kid-friendly line for a message from the laser safety monitor.</summary>
        public static string ForWarning(string message)
        {
            string text = message ?? string.Empty;
            if (Starts(text, "Stray light")) return "Oops, the beam is hitting something it shouldn't. Try tilting it back.";
            if (Starts(text, "Clipping")) return "The beam is hitting the edge of a mirror. Steer it back towards the middle.";
            if (Starts(text, "Actuator limit")) return "That mirror can't tilt any further. Try steering a different one.";
            if (Starts(text, "Back-reflection")) return "Some light is bouncing back into the laser. Try tilting that mirror a little.";
            return "Hmm, something looks off with the beam. Take a look at the monitor.";
        }

        /// <summary>What to say when a level is won. levelKey comes from LaserBriefingTexts.LevelKey.</summary>
        public static string ForWin(int stars, int levelKey, bool isLastLevel)
        {
            string praise = stars >= 3 ? "Yes, three stars!" : stars == 2 ? "Nice, two stars!" : "You did it!";
            string why;
            switch (levelKey)
            {
                case 0: why = " Both beams meet in the centre, just like in a real detector."; break;
                case 1: why = " You set both the beam's position and its direction, and that takes two mirrors."; break;
                case 2: why = " On arms this long, a tiny tilt moves the spot a lot, so that took real precision."; break;
                default: why = string.Empty; break;
            }
            return praise + why + (isLastLevel ? " That was the last level. Great work!" : string.Empty);
        }

        /// <summary>Which arrows to press, in words. Positive = right / up. Long distances become "hold".</summary>
        public static string ArrowPresses(int right, int up)
        {
            string horizontal = OneArrow(right, "right", "left");
            string vertical = OneArrow(up, "up", "down");
            if (horizontal == null) return vertical == null ? string.Empty : "Try " + vertical + ".";
            return "Try " + horizontal + (vertical == null ? "." : ", and " + vertical + ".");
        }

        private const int MostPressesToCount = 10;

        private static string OneArrow(int presses, string positive, string negative)
        {
            if (presses == 0) return null;
            string arrow = (presses > 0 ? positive : negative) + " arrow";
            int count = Math.Abs(presses);
            if (count > MostPressesToCount) return "holding the " + arrow;
            return "pressing the " + arrow + (count == 1 ? " once" : " " + count + " times");
        }

        private static bool Starts(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
