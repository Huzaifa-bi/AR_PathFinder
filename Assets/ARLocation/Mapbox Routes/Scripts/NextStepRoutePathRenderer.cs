using UnityEngine;

namespace ARLocation.MapboxRoutes
{
    /// <summary>
    /// Legacy sight-line renderer (LineAlignment.View). Disabled for AR — draws a black bar across the camera.
    /// AR navigation uses ARGuidanceSystem instead.
    /// </summary>
    public class NextStepRoutePathRenderer : AbstractRouteRenderer
    {
        [System.Serializable]
        public class SettingsData
        {
            public Material LineMaterial;
            public float TextureOffsetFactor = -4.0f;
        }

        public SettingsData Settings = new SettingsData();

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
            var stale = GameObject.Find("[NextStepRoutePathRenderer]");
            if (stale != null) Object.Destroy(stale);
        }
    }
}
