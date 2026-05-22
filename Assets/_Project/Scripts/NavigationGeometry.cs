using System;
using System.Collections.Generic;
using ARLocation;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>Distance from GPS point to route polyline (meters), for off-route detection.</summary>
    public static class NavigationGeometry
    {
        /// <summary>Shortest distance from P to any segment of the polyline (WGS84, local planar per segment).</summary>
        public static float MinDistanceMetersToPolyline(
            double plat, double plon,
            IList<Location> coords)
        {
            if (coords == null || coords.Count < 2) return float.MaxValue;

            float best = float.MaxValue;
            for (int i = 0; i < coords.Count - 1; i++)
            {
                var a = coords[i];
                var b = coords[i + 1];
                if (a == null || b == null) continue;
                float d = DistanceMetersPointToSegment(
                    plat, plon,
                    a.Latitude, a.Longitude,
                    b.Latitude, b.Longitude);
                if (d < best) best = d;
            }
            return best;
        }

        public static float DistanceMetersPointToSegment(
            double plat, double plon,
            double alat, double alon,
            double blat, double blon)
        {
            double rad = System.Math.PI / 180.0;
            double midLat = (alat + blat) * 0.5 * rad;
            double metersPerDegLat = 111320.0;
            double metersPerDegLon = 111320.0 * System.Math.Cos(midLat);

            double ax = 0, ay = 0;
            double bx = (blon - alon) * metersPerDegLon;
            double by = (blat - alat) * metersPerDegLat;
            double px = (plon - alon) * metersPerDegLon;
            double py = (plat - alat) * metersPerDegLat;

            double ab2 = bx * bx + by * by;
            if (ab2 < 1e-4)
                return (float)System.Math.Sqrt(px * px + py * py);

            double t = (px * bx + py * by) / ab2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;

            double qx = bx * t, qy = by * t;
            double dx = px - qx, dy = py - qy;
            return (float)System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Projects a GPS point onto the nearest route segment (WGS84 planar meters).</summary>
        public static bool TryProjectOntoPolyline(
            double plat, double plon,
            IList<Location> coords,
            out double projLat, out double projLon,
            out float distanceM)
        {
            projLat = plat;
            projLon = plon;
            distanceM = float.MaxValue;

            if (coords == null || coords.Count < 2) return false;

            bool found = false;
            for (int i = 0; i < coords.Count - 1; i++)
            {
                var a = coords[i];
                var b = coords[i + 1];
                if (a == null || b == null) continue;

                if (!TryProjectOntoSegment(
                        plat, plon,
                        a.Latitude, a.Longitude,
                        b.Latitude, b.Longitude,
                        out double t,
                        out float segDistM))
                    continue;

                if (segDistM >= distanceM) continue;

                distanceM = segDistM;
                projLat = a.Latitude + t * (b.Latitude - a.Latitude);
                projLon = a.Longitude + t * (b.Longitude - a.Longitude);
                found = true;
            }

            return found;
        }

        static bool TryProjectOntoSegment(
            double plat, double plon,
            double alat, double alon,
            double blat, double blon,
            out double t,
            out float distanceM)
        {
            t = 0;
            distanceM = float.MaxValue;

            double rad = System.Math.PI / 180.0;
            double midLat = (alat + blat) * 0.5 * rad;
            double metersPerDegLat = 111320.0;
            double metersPerDegLon = 111320.0 * System.Math.Cos(midLat);

            double bx = (blon - alon) * metersPerDegLon;
            double by = (blat - alat) * metersPerDegLat;
            double px = (plon - alon) * metersPerDegLon;
            double py = (plat - alat) * metersPerDegLat;

            double ab2 = bx * bx + by * by;
            if (ab2 < 1e-4)
            {
                distanceM = (float)System.Math.Sqrt(px * px + py * py);
                return true;
            }

            t = System.Math.Max(0, System.Math.Min(1, (px * bx + py * by) / ab2));
            double qx = bx * t, qy = by * t;
            double dx = px - qx, dy = py - qy;
            distanceM = (float)System.Math.Sqrt(dx * dx + dy * dy);
            return true;
        }

        /// <summary>
        /// Pulls the user fix onto the route centerline. Uses heading + route progress so corners
        /// snap to the correct leg (avoids 3–4 m sideways error at turns).
        /// </summary>
        public static Location SnapToRoute(
            Location user,
            IList<Location> coords,
            float maxSnapM,
            ref int routeSegmentIndex,
            float headingDeg = -1f)
        {
            if (user == null || coords == null || maxSnapM <= 0f) return user;

            if (!TryProjectOntoPolylineBest(
                    user.Latitude, user.Longitude, coords,
                    headingDeg, routeSegmentIndex,
                    out double lat, out double lon, out float dist, out int seg))
                return user;

            if (dist > maxSnapM)
                return user;

            routeSegmentIndex = seg;
            return new Location
            {
                Latitude = lat,
                Longitude = lon,
                Altitude = user.Altitude,
                AltitudeMode = user.AltitudeMode
            };
        }

        /// <summary>Nearest-segment projection (legacy; poor at sharp turns).</summary>
        public static Location SnapToRoute(Location user, IList<Location> coords, float maxSnapM)
        {
            int seg = -1;
            return SnapToRoute(user, coords, maxSnapM, ref seg);
        }

        /// <summary>
        /// Distance along the polyline (m) from the start to the best projection of P, plus lateral offset (m).
        /// </summary>
        public static bool TryGetArcLengthOnPolyline(
            double plat, double plon,
            IList<Location> coords,
            float headingDeg,
            ref int priorSegmentIndex,
            out float arcLengthM,
            out float lateralDistM)
        {
            arcLengthM = 0f;
            lateralDistM = float.MaxValue;

            if (!TryProjectOntoPolylineBest(
                    plat, plon, coords, headingDeg, priorSegmentIndex,
                    out double projLat, out double projLon, out lateralDistM, out int seg))
                return false;

            priorSegmentIndex = seg;
            var a = coords[seg];
            var b = coords[seg + 1];
            if (a == null || b == null) return false;

            if (!TryProjectOntoSegment(
                    plat, plon,
                    a.Latitude, a.Longitude,
                    b.Latitude, b.Longitude,
                    out double t, out _))
                return false;

            for (int i = 0; i < seg; i++)
            {
                var p0 = coords[i];
                var p1 = coords[i + 1];
                if (p0 == null || p1 == null) continue;
                arcLengthM += (float)CampusPathGraph.HaversineDistance(
                    p0.Latitude, p0.Longitude, p1.Latitude, p1.Longitude);
            }

            arcLengthM += (float)(t * (float)CampusPathGraph.HaversineDistance(
                a.Latitude, a.Longitude, b.Latitude, b.Longitude));
            return true;
        }

        static bool TryProjectOntoPolylineBest(
            double plat, double plon,
            IList<Location> coords,
            float headingDeg,
            int priorSegmentIndex,
            out double projLat, out double projLon,
            out float distanceM, out int bestSegment)
        {
            projLat = plat;
            projLon = plon;
            distanceM = float.MaxValue;
            bestSegment = -1;

            if (coords == null || coords.Count < 2) return false;

            float bestScore = float.MaxValue;
            bool found = false;

            for (int i = 0; i < coords.Count - 1; i++)
            {
                var a = coords[i];
                var b = coords[i + 1];
                if (a == null || b == null) continue;

                // At corners, never snap progress back onto a leg we already left.
                if (priorSegmentIndex >= 0 && i < priorSegmentIndex)
                    continue;

                if (!TryProjectOntoSegment(
                        plat, plon,
                        a.Latitude, a.Longitude,
                        b.Latitude, b.Longitude,
                        out double t,
                        out float segDistM))
                    continue;

                float score = segDistM;

                if (headingDeg >= 0f)
                {
                    float segBearing = SegmentBearingDegrees(
                        a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                    float bearingDiff = Mathf.Abs(Mathf.DeltaAngle(headingDeg, segBearing));
                    if (bearingDiff > 100f)
                        score += 25f;
                    else
                        score += bearingDiff * 0.08f;

                    if (t < 0.05f && bearingDiff > 45f)
                        score += 8f;
                }

                if (priorSegmentIndex >= 0)
                {
                    if (i == priorSegmentIndex || i == priorSegmentIndex + 1)
                        score -= 2f;
                    else if (i > priorSegmentIndex + 1)
                        score += 6f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    distanceM = segDistM;
                    projLat = a.Latitude + t * (b.Latitude - a.Latitude);
                    projLon = a.Longitude + t * (b.Longitude - a.Longitude);
                    bestSegment = i;
                    found = true;
                }
            }

            if (found)
                TryAdvanceSegmentAtCorner(
                    plat, plon, coords, headingDeg, priorSegmentIndex,
                    ref bestSegment, ref projLat, ref projLon, ref distanceM);

            return found;
        }

        /// <summary>Interior angle (degrees) at polyline vertex index. 0 on endpoints.</summary>
        public static float GetTurnAngleAtVertex(IList<Location> coords, int vertexIndex)
        {
            if (coords == null || vertexIndex <= 0 || vertexIndex >= coords.Count - 1)
                return 0f;

            var p0 = coords[vertexIndex - 1];
            var p1 = coords[vertexIndex];
            var p2 = coords[vertexIndex + 1];
            if (p0 == null || p1 == null || p2 == null) return 0f;

            float inBearing = SegmentBearingDegrees(
                p0.Latitude, p0.Longitude, p1.Latitude, p1.Longitude);
            float outBearing = SegmentBearingDegrees(
                p1.Latitude, p1.Longitude, p2.Latitude, p2.Longitude);
            return Mathf.Abs(Mathf.DeltaAngle(inBearing, outBearing));
        }

        /// <summary>Turn angle at the end of segment <paramref name="segmentIndex"/> (degrees).</summary>
        public static float GetTurnAngleAfterSegment(IList<Location> coords, int segmentIndex)
        {
            if (coords == null || segmentIndex < 0) return 0f;
            return GetTurnAngleAtVertex(coords, segmentIndex + 1);
        }

        static void TryAdvanceSegmentAtCorner(
            double plat, double plon,
            IList<Location> coords,
            float headingDeg,
            int priorSegmentIndex,
            ref int bestSegment,
            ref double projLat, ref double projLon,
            ref float distanceM)
        {
            if (priorSegmentIndex < 0 || bestSegment != priorSegmentIndex || bestSegment >= coords.Count - 2)
                return;

            float turnDeg = GetTurnAngleAtVertex(coords, bestSegment + 1);
            if (turnDeg < PIEASConfig.CornerHandoffAngleGentleDeg)
                return;

            var a = coords[bestSegment];
            var b = coords[bestSegment + 1];
            var c = coords[bestSegment + 2];
            if (a == null || b == null || c == null) return;

            if (!TryProjectOntoSegment(plat, plon, a.Latitude, a.Longitude, b.Latitude, b.Longitude,
                    out double tEnd, out _))
                return;

            float tHandoff = CornerHandoffThreshold(turnDeg);
            float slackM = CornerNextSegmentSlackM(turnDeg);

            if (tEnd < tHandoff) return;

            if (!TryProjectOntoSegment(plat, plon, b.Latitude, b.Longitude, c.Latitude, c.Longitude,
                    out double tNext, out float nextDist))
                return;

            if (nextDist > distanceM + slackM) return;

            if (headingDeg >= 0f)
            {
                float nextBearing = SegmentBearingDegrees(b.Latitude, b.Longitude, c.Latitude, c.Longitude);
                float bearingDiff = Mathf.Abs(Mathf.DeltaAngle(headingDeg, nextBearing));
                float maxBearing = turnDeg >= PIEASConfig.CornerHandoffAngleSharpDeg ? 95f : 48f;
                if (bearingDiff > maxBearing) return;
            }

            bestSegment++;
            distanceM = nextDist;
            projLat = b.Latitude + tNext * (c.Latitude - b.Latitude);
            projLon = b.Longitude + tNext * (c.Longitude - b.Longitude);
        }

        static float CornerHandoffThreshold(float turnDeg)
        {
            float sharp = PIEASConfig.CornerHandoffAngleSharpDeg;
            float gentle = PIEASConfig.CornerHandoffAngleGentleDeg;
            if (turnDeg <= gentle) return PIEASConfig.CornerHandoffTAtGentle;
            if (turnDeg >= sharp) return PIEASConfig.CornerHandoffTAtSharp;
            float u = (turnDeg - gentle) / Mathf.Max(1f, sharp - gentle);
            return Mathf.Lerp(PIEASConfig.CornerHandoffTAtGentle, PIEASConfig.CornerHandoffTAtSharp, u);
        }

        static float CornerNextSegmentSlackM(float turnDeg)
        {
            float sharp = PIEASConfig.CornerHandoffAngleSharpDeg;
            float gentle = PIEASConfig.CornerHandoffAngleGentleDeg;
            if (turnDeg <= gentle) return PIEASConfig.CornerNextSegSlackGentleM;
            if (turnDeg >= sharp) return PIEASConfig.CornerNextSegSlackSharpM;
            float u = (turnDeg - gentle) / Mathf.Max(1f, sharp - gentle);
            return Mathf.Lerp(PIEASConfig.CornerNextSegSlackGentleM, PIEASConfig.CornerNextSegSlackSharpM, u);
        }

        static float SegmentBearingDegrees(double alat, double alon, double blat, double blon)
        {
            double rad = Math.PI / 180.0;
            double midLat = (alat + blat) * 0.5 * rad;
            double east = (blon - alon) * 111320.0 * Math.Cos(midLat);
            double north = (blat - alat) * 111320.0;
            double deg = Math.Atan2(east, north) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            return (float)deg;
        }

        /// <summary>Drop dense polyline vertices so AR chevrons follow smooth campus roads.</summary>
        public static List<Location> SimplifyPolylineForDisplay(IList<Location> src, double minSpacingM = 2.5)
        {
            return SimplifyPolylineForNavigation(src, minSpacingM, PIEASConfig.PathSimplifySpacingCurveM);
        }

        /// <summary>
        /// Simplify for AR navigation: keep tight spacing on curves, normal spacing on straights,
        /// and always preserve maneuver vertices.
        /// </summary>
        public static List<Location> SimplifyPolylineForNavigation(
            IList<Location> src,
            double straightSpacingM = 2.5,
            double curveSpacingM = 1.6)
        {
            var dst = new List<Location>();
            if (src == null || src.Count == 0) return dst;

            Location last = null;
            for (int i = 0; i < src.Count; i++)
            {
                var p = src[i];
                if (p == null) continue;

                bool isManeuverVertex = i > 0 && i < src.Count - 1 &&
                    GetTurnAngleAtVertex(src, i) >= PIEASConfig.CornerPreserveAngleDeg;
                double minSpacing = isManeuverVertex ? curveSpacingM : straightSpacingM;

                if (last == null)
                {
                    dst.Add(p.Clone());
                    last = p;
                    continue;
                }

                if (isManeuverVertex)
                {
                    double dCorner = CampusPathGraph.HaversineDistance(
                        last.Latitude, last.Longitude, p.Latitude, p.Longitude);
                    if (dCorner > 0.35)
                    {
                        dst.Add(p.Clone());
                        last = p;
                    }
                    continue;
                }

                double d = CampusPathGraph.HaversineDistance(
                    last.Latitude, last.Longitude, p.Latitude, p.Longitude);
                if (d >= minSpacing)
                {
                    dst.Add(p.Clone());
                    last = p;
                }
            }

            var end = src[src.Count - 1];
            if (end != null && (dst.Count == 0 || dst[dst.Count - 1] != end))
            {
                if (dst.Count > 0)
                {
                    double tail = CampusPathGraph.HaversineDistance(
                        dst[dst.Count - 1].Latitude, dst[dst.Count - 1].Longitude,
                        end.Latitude, end.Longitude);
                    if (tail > 0.5) dst.Add(end.Clone());
                }
                else dst.Add(end.Clone());
            }

            return dst;
        }
    }
}
