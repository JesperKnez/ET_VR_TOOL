using System.Globalization;
using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>Small helpers to turn measurements into readable text (for UI and the AI tutor).</summary>
    public static class LaserText
    {
        private static readonly CultureInfo s_Culture = CultureInfo.InvariantCulture;

        /// <summary>"2.1 cm left, 0.4 cm up" for an offset in metres (x = right, y = up).</summary>
        public static string DescribeOffset(Vector2 offsetMeters)
        {
            string horizontal = DescribeAxis(offsetMeters.x, "right", "left");
            string vertical = DescribeAxis(offsetMeters.y, "up", "down");

            if (horizontal == null && vertical == null)
            {
                return "centred";
            }

            if (horizontal == null)
            {
                return vertical;
            }

            return vertical == null ? horizontal : horizontal + ", " + vertical;
        }

        /// <summary>Formats a number with a fixed number of decimals, always with a dot.</summary>
        public static string Number(float value, int decimals)
        {
            return value.ToString("F" + decimals, s_Culture);
        }

        /// <summary>Formats a number with an explicit sign, e.g. "+0.50".</summary>
        public static string SignedNumber(float value, int decimals)
        {
            string formatted = Mathf.Abs(value).ToString("F" + decimals, s_Culture);
            return (value < 0f ? "-" : "+") + formatted;
        }

        /// <summary>"42%" for 0.42.</summary>
        public static string Percent(float fraction)
        {
            return (fraction * 100f).ToString("F0", s_Culture) + "%";
        }

        private static string DescribeAxis(float meters, string positiveWord, string negativeWord)
        {
            float centimetres = meters * 100f;
            if (Mathf.Abs(centimetres) < 0.05f)
            {
                return null;
            }

            return Number(Mathf.Abs(centimetres), 1) + " cm " + (centimetres > 0f ? positiveWord : negativeWord);
        }
    }
}
