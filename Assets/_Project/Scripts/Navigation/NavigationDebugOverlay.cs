using ARLocation;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject.Navigation
{
    /// <summary>
    /// On-screen GPS / heading / origin debug (enable on NavigationSensorFusion or this component).
    /// </summary>
    public class NavigationDebugOverlay : MonoBehaviour
    {
        public bool ShowOverlay = true;
        public KeyCode ToggleKey = KeyCode.F1;

        NavigationSensorFusion _fusion;
        ARGuidanceSystem _guidance;
        GUIStyle _style;

        void Awake()
        {
            _fusion = NavigationSensorFusion.Instance ??
                      FindObjectOfType<NavigationSensorFusion>();
            _guidance = FindObjectOfType<ARGuidanceSystem>();
        }

        void Update()
        {
            if (Input.GetKeyDown(ToggleKey))
                ShowOverlay = !ShowOverlay;
        }

        void OnGUI()
        {
            if (!ShowOverlay || _fusion == null) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 22,
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = Color.white }
                };
            }

            var o = _fusion.Origin;
            var sm = _fusion.SmoothedLocation;
            string text =
                $"GPS acc: {_fusion.LastHorizontalAccuracyM:F1} m\n" +
                $"Heading: {_fusion.SmoothedHeadingDegrees:F1}°\n" +
                $"Origin: {(o.IsLocked ? $"{o.Origin.Latitude:F6}, {o.Origin.Longitude:F6}" : "—")}\n" +
                $"Smooth: {(sm != null ? $"{sm.Latitude:F6}, {sm.Longitude:F6}" : "—")}\n" +
                $"AR session: {UnityEngine.XR.ARFoundation.ARSession.state}\n" +
                $"Guidance: {(_guidance != null && _guidance.IsActive ? _guidance.LastStatus : "off")}\n" +
                $"Chevrons: {(_guidance != null ? _guidance.LastChevronCount : 0)}  Path pts: {(_guidance != null ? _guidance.LastWorldPathPointCount : 0)}";

            GUI.Box(new Rect(12, 120, 520, 200), text, _style);
        }

        void OnDrawGizmos()
        {
            if (!ShowOverlay || _fusion == null || !_fusion.HasFix) return;
            var arRoot = ARLocationManager.Instance?.transform;
            var cam = UnityEngine.Camera.main;
            if (arRoot == null || cam == null) return;

            Vector3 camPos = cam.transform.position;
            var world = _fusion.Origin.LocalEnuToWorld(arRoot, camPos, Vector3.zero);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(world, 0.5f);
            Gizmos.color = Color.yellow;
            var sm = _fusion.SmoothedLocation;
            if (sm != null)
            {
                var p = _fusion.Origin.GeoToLocalEnu(sm);
                Gizmos.DrawWireSphere(_fusion.Origin.LocalEnuToWorld(arRoot, camPos, p), 0.35f);
            }
        }
    }
}
