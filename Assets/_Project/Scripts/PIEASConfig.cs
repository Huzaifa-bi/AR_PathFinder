using UnityEngine;
using Mapbox.Utils;

// PIEAS-specific configuration for AR_PathFinder
namespace ARLocation.MapboxRoutes.SampleProject
{
    public static class PIEASConfig
    {
        /// <summary>Directions API vendor.</summary>
        public enum DirectionsProvider
        {
            MapboxWalking = 0,
            GoogleDirections = 1
        }

        /// <summary>
        /// Driving = follows motorable campus roads (matches “stay on the road”).
        /// Walking = footpaths, shortcuts, and pedestrian-only links.
        /// </summary>
        public enum RouteTravelProfile
        {
            Driving = 0,
            Walking = 1
        }

        /// <summary>Google Routes API (set GoogleMapsApiKey in Secrets.json; enable Routes API in Cloud Console).</summary>
        public const DirectionsProvider ActiveDirectionsProvider = DirectionsProvider.GoogleDirections;

        /// <summary>Use roads, not pedestrian cut-through paths.</summary>
        public const RouteTravelProfile ActiveTravelProfile = RouteTravelProfile.Driving;

        /// <summary>Routes API travel mode: DRIVE or WALK.</summary>
        public static string GoogleRoutesTravelMode =>
            ActiveTravelProfile == RouteTravelProfile.Driving ? "DRIVE" : "WALK";

        public static string MapboxDirectionsProfile =>
            ActiveTravelProfile == RouteTravelProfile.Driving ? "driving" : "walking";

        // University Information
        public const string UniversityName = "Pakistan Institute of Engineering and Applied Sciences";
        public const string UniversityShortName = "PIEAS";
        public const string UniversityWebsite = "pieas.edu.pk";
        public const string AppTitle = "PIEAS Campus Navigation";

        // Mapbox API Token — loaded from StreamingAssets/Secrets.json at runtime.
        // Fallback hardcoded value used if Secrets.json is missing or unreadable.
        private const string FallbackMapboxToken = "YOUR_MAPBOX_TOKEN_HERE";
        private const string FallbackGoogleMapsApiKey = "";
        private static string _loadedToken;
        private static string _googleMapsApiKey;
        private static bool _secretsTriedLoad;

        public static string MapboxToken
        {
            get
            {
                EnsureSecretsLoaded();
                return _loadedToken;
            }
        }

        /// <summary>Google Maps Platform key for Routes API (see GoogleDirectionsRouteClient).</summary>
        public static string GoogleMapsApiKey
        {
            get
            {
                EnsureSecretsLoaded();
                return _googleMapsApiKey ?? string.Empty;
            }
        }

        static void EnsureSecretsLoaded()
        {
            if (_secretsTriedLoad) return;
            _secretsTriedLoad = true;
            LoadSecretsFromDisk();
        }

        /// <summary>
        /// Load Mapbox + optional Google keys from StreamingAssets/Secrets.json.
        /// Expected format: { "MapboxToken": "pk.xxx", "GoogleMapsApiKey": "AIza..." }
        /// Falls back to the hardcoded Mapbox token if file is missing or parse fails.
        /// </summary>
        private static void LoadSecretsFromDisk()
        {
            _loadedToken = FallbackMapboxToken;
            _googleMapsApiKey = null;

            try
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, "Secrets.json");
                string json = ReadStreamingAssetsText(path);
                if (!string.IsNullOrEmpty(json))
                {
                    var data = JsonUtility.FromJson<SecretsData>(json);
                    if (!string.IsNullOrEmpty(data?.MapboxToken))
                    {
                        Debug.Log("[PIEASConfig] Mapbox token loaded from Secrets.json");
                        _loadedToken = data.MapboxToken;
                    }
                    if (!string.IsNullOrEmpty(data?.GoogleMapsApiKey))
                    {
                        Debug.Log("[PIEASConfig] GoogleMapsApiKey loaded from Secrets.json");
                        _googleMapsApiKey = data.GoogleMapsApiKey;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PIEASConfig] Could not load Secrets.json: {ex.Message}");
            }

            if (_loadedToken == FallbackMapboxToken)
                Debug.Log("[PIEASConfig] Using fallback Mapbox token (Secrets.json missing or no MapboxToken)");
            if (string.IsNullOrEmpty(_googleMapsApiKey) && !string.IsNullOrEmpty(FallbackGoogleMapsApiKey))
                _googleMapsApiKey = FallbackGoogleMapsApiKey;
        }

        static string ReadStreamingAssetsText(string fullPath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!fullPath.Contains("://"))
                fullPath = "file://" + fullPath;
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(fullPath))
            {
                req.SendWebRequest();
                while (!req.isDone) { }
#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                    return null;
                return req.downloadHandler?.text;
            }
#else
            if (System.IO.File.Exists(fullPath))
                return System.IO.File.ReadAllText(fullPath);
            return null;
#endif
        }

        [System.Serializable]
        private class SecretsData
        {
            public string MapboxToken;
            public string GoogleMapsApiKey;
        }

        // Map Configuration
        public const float CenterLatitude = 33.65598735240187f;
        public const float CenterLongitude = 73.2649697331715f;
        public const int DefaultZoomLevel = 16;
        public const int DefaultMapSize = 400;

        // Branding Colors (Hex values converted to Color)
        public static Color PrimaryColor => HexToColor("#C41E3A");      // Dark Red
        public static Color SecondaryColor => HexToColor("#FFFFFF");    // White
        public static Color RouteLineColor => HexToColor("#FF6B6B");    // Bright Red
        public const float RouteLineWidth = 3f;

        // Campus Bounds
        // NE Corner (Top-Right) — include eastern gates (e.g. Barrier 3 ~73.2745E)
        public const float BoundsNE_Latitude = 33.65680081755661f;
        public const float BoundsNE_Longitude = 73.285f;

        // SW Corner (Bottom-Left)
        public const float BoundsSW_Latitude = 33.652851666836256f;
        public const float BoundsSW_Longitude = 73.2632557794565f;

        public static Vector2d CampusCenter => new Vector2d(CenterLatitude, CenterLongitude);
        public static Vector2d BoundsNE => new Vector2d(BoundsNE_Latitude, BoundsNE_Longitude);
        public static Vector2d BoundsSW => new Vector2d(BoundsSW_Latitude, BoundsSW_Longitude);

        /// <summary>
        /// Satellite + street labels on the Mapbox base map (Google-Earth–style backdrop; routing still uses APIs / graph, not pixels).
        /// </summary>
        public const bool UseSatelliteStreetBasemap = true;

        /// <summary>
        /// When <see cref="ActiveTravelProfile"/> is Walking and both endpoints are on campus, prefer API directions
        /// over the hand-drawn walkway graph (graph is fallback only). Ignored when profile is Driving.
        /// </summary>
        public const bool MapboxWalkingFirstOnCampus = true;

        // Check if a location is within campus bounds
        public static bool IsWithinCampusBounds(Vector2d location)
        {
            bool latitudeInBounds = location.x >= BoundsSW_Latitude && location.x <= BoundsNE_Latitude;
            bool longitudeInBounds = location.y >= BoundsSW_Longitude && location.y <= BoundsNE_Longitude;

            return latitudeInBounds && longitudeInBounds;
        }

        /// <summary>AR chevron height above ground plane at PIEAS (flat campus).</summary>
        public const float CampusPathGroundOffsetM = 0.04f;

        /// <summary>Typical phone height when walking (camera Y minus ground Y).</summary>
        public const float PhoneHeightAboveGroundM = 1.48f;

        /// <summary>Min distance between displayed path vertices (reduces GPS zig-zag).</summary>
        public const float CampusPathSimplifySpacingM = 2.5f;

        /// <summary>Soft / hard off-route thresholds tuned for campus roads (meters).</summary>
        public const float CampusOffRouteWarningM = 12f;
        public const float CampusOffRouteRecalcM = 22f;

        /// <summary>Snap smoothed GPS to route centerline for AR placement (phone GPS is often on the footpath).</summary>
        public const float PathBakeSnapMaxM = 10f;

        // ── Corner / curve tuning (sharp 90° turns + gentle campus curves) ───────

        /// <summary>Keep extra polyline points when turn angle at a vertex exceeds this (degrees).</summary>
        public const float CornerPreserveAngleDeg = 12f;

        /// <summary>Tighter vertex spacing on curved legs (m); straights use CampusPathSimplifySpacingM.</summary>
        public const float PathSimplifySpacingCurveM = 1.6f;

        /// <summary>Turn angle (deg) treated as a sharp corner for early segment hand-off.</summary>
        public const float CornerHandoffAngleSharpDeg = 55f;

        /// <summary>Turn angle (deg) treated as a gentle curve for late hand-off.</summary>
        public const float CornerHandoffAngleGentleDeg = 18f;

        /// <summary>Along-segment progress (0–1) before advancing to next leg at a sharp turn.</summary>
        public const float CornerHandoffTAtSharp = 0.74f;

        /// <summary>Along-segment progress (0–1) before advancing at a gentle curve.</summary>
        public const float CornerHandoffTAtGentle = 0.94f;

        /// <summary>Max extra lateral error (m) allowed when jumping to the next leg at a sharp turn.</summary>
        public const float CornerNextSegSlackSharpM = 7f;

        /// <summary>Max extra lateral error (m) when advancing on a gentle curve.</summary>
        public const float CornerNextSegSlackGentleM = 2.5f;

        /// <summary>Re-bake AR path when a single vertex turn exceeds this (degrees).</summary>
        public const float CornerRebakeMinAngleDeg = 16f;

        /// <summary>Re-bake after several gentle corners whose angles sum past this (degrees).</summary>
        public const float CornerRebakeAccumulatedAngleDeg = 38f;

        /// <summary>Course-over-ground blend on gentle curves (more walk direction, less compass).</summary>
        public const float ProgressCogBlendGentle = 0.58f;

        /// <summary>Course-over-ground blend on sharp turns.</summary>
        public const float ProgressCogBlendSharp = 0.38f;

        public const float ProgressMinMoveForCogM = 0.75f;

        /// <summary>Max arc-length catch-up per second on sharp turns (m).</summary>
        public const float ArcCatchUpMaxStepSharpM = 4.5f;

        /// <summary>Max arc-length catch-up per second on gentle curves (m).</summary>
        public const float ArcCatchUpMaxStepGentleM = 2.5f;

        /// <summary>Only use heading / snap for progress when a turn this sharp is ahead (degrees).</summary>
        public const float CornerProgressHeadingMinDeg = 22f;

        // Convert Hex color string to Unity Color
        private static Color HexToColor(string hex)
        {
            hex = hex.Replace("#", "");
            byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
