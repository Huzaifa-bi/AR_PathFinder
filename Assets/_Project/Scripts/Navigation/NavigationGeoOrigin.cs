using ARLocation;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject.Navigation
{
    /// <summary>
    /// Session-local ENU origin. Route geometry is stored relative to this point so
    /// per-frame GPS jitter does not slide the entire path sideways.
    /// </summary>
    public sealed class NavigationGeoOrigin
    {
        public bool IsLocked { get; private set; }
        public Location Origin { get; private set; }

        public void Lock(Location gps)
        {
            if (gps == null) return;
            Origin = gps.Clone();
            IsLocked = true;
        }

        public void Reset()
        {
            IsLocked = false;
            Origin = null;
        }

        /// <summary>East (X), up (Y), north (Z) meters from origin.</summary>
        public Vector3 GeoToLocalEnu(Location geo, bool ignoreAltitude = true)
        {
            if (!IsLocked || geo == null) return Vector3.zero;
            var v = Location.VectorFromTo(Origin, geo, ignoreAltitude);
            return new Vector3((float)v.x, (float)v.y, (float)v.z);
        }

        public Vector3 LocalEnuToWorld(Transform arRoot, Vector3 cameraPosition, Vector3 localEnu)
        {
            if (arRoot == null) return cameraPosition + localEnu;
            return cameraPosition + arRoot.TransformVector(localEnu);
        }

        public Vector3 GeoToWorld(Transform arRoot, Transform camera, Location geo, bool ignoreAltitude = true)
        {
            if (camera == null) return Vector3.zero;
            return LocalEnuToWorld(arRoot, camera.position, GeoToLocalEnu(geo, ignoreAltitude));
        }
    }
}
