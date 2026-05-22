using UnityEngine;
using ARLocation.MapboxRoutes;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>Turn Mapbox-style steps into clearer turn-by-turn copy (cardinal → relative where possible).</summary>
    public static class NavigationInstructionFormatter
    {
        /// <param name="userHeadingDeg">Device compass true heading (0–360), or negative if unavailable.</param>
        public static string BuildBannerLine(
            Route.Step currentStep,
            Route.Step nextStep,
            Route.Maneuver nextManeuver,
            float distToNextManeuverMeters,
            string destinationDisplayName,
            float userHeadingDeg)
        {
            bool hasNext = nextManeuver != null && distToNextManeuverMeters >= 0;

            if (!hasNext)
            {
                if (!string.IsNullOrEmpty(destinationDisplayName))
                    return $"Follow the path toward {destinationDisplayName}";
                return "Follow the highlighted path";
            }

            string street = PickStreetName(currentStep, nextStep);
            string dist = FormatDistance(distToNextManeuverMeters);
            string cleaned = CleanInstruction(nextManeuver.instruction);

            // Prefer Mapbox step text — compass-relative "bear left" + "Turn right" was confusing.
            if (!string.IsNullOrEmpty(cleaned))
            {
                if (!string.IsNullOrEmpty(street))
                    return $"{dist}, {cleaned} — then along {street}";
                return $"{dist}, {cleaned}";
            }

            string turnWord = TurnWordFromManeuver(nextManeuver, userHeadingDeg);
            if (!string.IsNullOrEmpty(street))
                return $"{dist}, {turnWord} — then along {street}";

            return $"{dist}, {turnWord}";
        }

        static string PickStreetName(Route.Step cur, Route.Step next)
        {
            if (next != null && !string.IsNullOrWhiteSpace(next.name))
                return next.name.Trim();
            if (cur != null && !string.IsNullOrWhiteSpace(cur.name))
                return cur.name.Trim();
            return null;
        }

        public static string BuildContinueToward(string destinationDisplayName)
        {
            if (string.IsNullOrEmpty(destinationDisplayName))
                return "Continue on the highlighted path";
            return $"Continue toward {destinationDisplayName}";
        }

        static string TurnWordFromManeuver(Route.Maneuver m, float userHeadingDeg)
        {
            string type = (m.type ?? "").ToLowerInvariant();
            if (type == "arrive" || type == "end")
                return "Arrive";

            int bearing = m.bearing_before;
            if (userHeadingDeg >= 0f && userHeadingDeg <= 360f && bearing >= 0 && bearing <= 360)
            {
                float delta = NormalizeAngle180(bearing - userHeadingDeg);
                if (delta > -35 && delta < 35) return "go straight ahead";
                if (delta >= 35 && delta < 85) return "bear right";
                if (delta >= 85 && delta < 135) return "turn right";
                if (Mathf.Abs(delta) >= 135f) return "make a U-turn";
                if (delta <= -35 && delta > -85) return "bear left";
                if (delta <= -85 && delta > -135) return "turn left";
            }

            if (bearing >= 0 && bearing <= 360)
            {
                string card = CardinalFromBearing(bearing);
                return $"head {card}";
            }

            return VerbFromInstruction(m.instruction);
        }

        static string VerbFromInstruction(string instr)
        {
            if (string.IsNullOrEmpty(instr)) return "continue";
            string s = instr.ToLowerInvariant();
            if (s.Contains("u-turn") || s.Contains("uturn")) return "make a U-turn";
            if (s.Contains("sharp right")) return "turn sharp right";
            if (s.Contains("sharp left")) return "turn sharp left";
            if (s.Contains("slight right")) return "bear right";
            if (s.Contains("slight left")) return "bear left";
            if (s.Contains("right")) return "turn right";
            if (s.Contains("left")) return "turn left";
            return "continue";
        }

        static string CardinalFromBearing(int bearing)
        {
            string[] dirs = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest", "north" };
            int idx = (int)System.Math.Round(bearing / 45.0) % 8;
            return dirs[idx];
        }

        static float NormalizeAngle180(float deg)
        {
            while (deg > 180f) deg -= 360f;
            while (deg < -180f) deg += 360f;
            return deg;
        }

        static string CleanInstruction(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Trim();
            // Drop leading "Walk north," style noise if we already gave a turn word
            int comma = s.IndexOf(',');
            if (comma > 0 && comma < s.Length - 1)
            {
                string tail = s.Substring(comma + 1).Trim();
                if (tail.Length > 3) return char.ToUpper(tail[0]) + tail.Substring(1);
            }
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        static string FormatDistance(float meters)
        {
            if (meters >= 1000f) return $"In {meters / 1000f:F1} km";
            int rounded = ((int)System.Math.Round(meters / 5.0)) * 5;
            if (rounded < 10) rounded = (int)System.Math.Max(5, meters);
            return $"In {rounded} m";
        }

        /// <summary>Arrow glyph for the top banner — always matches the written turn instruction.</summary>
        public static string ArrowForManeuver(Route.Maneuver m, float userHeadingDeg)
        {
            if (m == null) return "↑";
            string type = (m.type ?? "").ToLowerInvariant();
            if (type == "arrive" || type == "end") return "◉";

            string fromInstr = ArrowFromInstructionText(m.instruction);
            if (fromInstr != null)
                return fromInstr;

            string fromType = ArrowFromManeuverType(m.type);
            if (fromType != null)
                return fromType;

            if (m.bearing_before >= 0 && m.bearing_before <= 360 &&
                m.bearing_after >= 0 && m.bearing_after <= 360)
            {
                float turnDelta = NormalizeAngle180(m.bearing_after - m.bearing_before);
                if (Mathf.Abs(turnDelta) >= 135f) return "↩";
                if (turnDelta > 35f && turnDelta < 145f) return "→";
                if (turnDelta < -35f && turnDelta > -145f) return "←";
                if (Mathf.Abs(turnDelta) <= 35f) return "↑";
            }

            if (userHeadingDeg >= 0f && userHeadingDeg <= 360f &&
                m.bearing_after >= 0 && m.bearing_after <= 360)
            {
                float delta = NormalizeAngle180(m.bearing_after - userHeadingDeg);
                if (Mathf.Abs(delta) >= 135f) return "↩";
                if (delta > 40f && delta < 130f) return "→";
                if (delta < -40f && delta > -130f) return "←";
                if (delta > -40f && delta < 40f) return "↑";
            }

            return "↑";
        }

        static string ArrowFromInstructionText(string instr)
        {
            if (string.IsNullOrEmpty(instr)) return null;
            string s = instr.ToLowerInvariant();
            if (s.Contains("u-turn") || s.Contains("uturn")) return "↩";
            if (s.Contains("sharp right")) return "↱";
            if (s.Contains("sharp left")) return "↰";
            if (s.Contains("slight right")) return "↗";
            if (s.Contains("slight left")) return "↖";
            if (s.Contains("right")) return "→";
            if (s.Contains("left")) return "←";
            return null;
        }

        static string ArrowFromManeuverType(string maneuverType)
        {
            if (string.IsNullOrEmpty(maneuverType)) return null;
            string t = maneuverType.ToLowerInvariant();
            if (t.Contains("uturn")) return "↩";
            if (t.Contains("right")) return "→";
            if (t.Contains("left")) return "←";
            return null;
        }
    }
}
