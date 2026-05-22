using System.Collections.Generic;
using ARLocation;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject.Navigation
{
    /// <summary>
    /// Resamples a geographic polyline into smooth local ENU points (Catmull-Rom).
    /// </summary>
    public static class RoutePathSpline
    {
        public static List<Vector3> BuildLocalEnuPath(
            NavigationGeoOrigin origin,
            IList<Location> route,
            int samplesPerSegment = 6,
            float tension = 0.5f)
        {
            var result = new List<Vector3>();
            if (origin == null || !origin.IsLocked || route == null || route.Count < 2)
                return result;

            var control = new List<Vector3>();
            for (int i = 0; i < route.Count; i++)
            {
                if (route[i] == null) continue;
                control.Add(origin.GeoToLocalEnu(route[i]));
            }

            if (control.Count < 2) return result;

            int n = Mathf.Max(2, samplesPerSegment);
            for (int seg = 0; seg < control.Count - 1; seg++)
            {
                Vector3 p0 = seg == 0 ? control[0] : control[seg - 1];
                Vector3 p1 = control[seg];
                Vector3 p2 = control[seg + 1];
                Vector3 p3 = seg + 2 < control.Count ? control[seg + 2] : control[seg + 1];

                int startJ = seg == 0 ? 0 : 1;
                for (int j = startJ; j <= n; j++)
                {
                    float t = j / (float)n;
                    var pt = CatmullRom(p0, p1, p2, p3, t, tension);
                    if (result.Count == 0 || (result[result.Count - 1] - pt).sqrMagnitude > 0.04f)
                        result.Add(pt);
                }
            }

            return result;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, float alpha)
        {
            float t0 = 0f;
            float t1 = GetT(t0, p0, p1, alpha);
            float t2 = GetT(t1, p1, p2, alpha);
            float t3 = GetT(t2, p2, p3, alpha);
            t = Mathf.Lerp(t1, t2, t);
            return Remap(p0, p1, p2, p3, t0, t1, t2, t3, t);
        }

        static float GetT(float t, Vector3 a, Vector3 b, float alpha)
        {
            float d = (b - a).magnitude;
            return t + Mathf.Pow(d, alpha);
        }

        static Vector3 Remap(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float t0, float t1, float t2, float t3, float t)
        {
            Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }
    }
}
