using UnityEngine;

namespace ARLocation.MapboxRoutes
{
    /// <summary>
    /// Legacy ground LineRenderer path. Disabled for AR — use ARGuidanceSystem instead.
    /// </summary>
    public class PathRouteRenderer : AbstractRouteRenderer
    {
        [System.Serializable]
        public class SettingsData
        {
            public Material LineMaterial;
            public float PathWidth = 1.15f;
        }

        public SettingsData Settings;

        void Awake() => DestroyLegacyLine();
        void OnEnable()
        {
            enabled = false;
            DestroyLegacyLine();
        }

        public override void Init(RoutePathRendererArgs args) { }

        public override void OnRouteUpdate(RoutePathRendererArgs args) { }

        static void DestroyLegacyLine()
        {
            var stale = GameObject.Find("[RoutePathRenderer]");
            if (stale != null) Object.Destroy(stale);
        }
    }
}
