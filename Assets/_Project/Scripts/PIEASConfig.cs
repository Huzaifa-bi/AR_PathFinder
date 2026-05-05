using UnityEngine;
using Mapbox.Utils;

// PIEAS-specific configuration for AR_PathFinder
namespace ARLocation.MapboxRoutes.SampleProject
{
    public static class PIEASConfig
    {
        // University Information
        public const string UniversityName = "Pakistan Institute of Engineering and Applied Sciences";
        public const string UniversityShortName = "PIEAS";
        public const string UniversityWebsite = "pieas.edu.pk";
        public const string AppTitle = "PIEAS Campus Navigation";

        // Mapbox API Token (centralized — all scripts should reference this)
        public const string MapboxToken = "YOUR_MAPBOX_TOKEN";

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
        // NE Corner (Top-Right)
        public const float BoundsNE_Latitude = 33.65680081755661f;
        public const float BoundsNE_Longitude = 73.26840212624316f;

        // SW Corner (Bottom-Left)
        public const float BoundsSW_Latitude = 33.652851666836256f;
        public const float BoundsSW_Longitude = 73.2632557794565f;

        public static Vector2d CampusCenter => new Vector2d(CenterLatitude, CenterLongitude);
        public static Vector2d BoundsNE => new Vector2d(BoundsNE_Latitude, BoundsNE_Longitude);
        public static Vector2d BoundsSW => new Vector2d(BoundsSW_Latitude, BoundsSW_Longitude);

        // Check if a location is within campus bounds
        public static bool IsWithinCampusBounds(Vector2d location)
        {
            bool latitudeInBounds = location.x >= BoundsSW_Latitude && location.x <= BoundsNE_Latitude;
            bool longitudeInBounds = location.y >= BoundsSW_Longitude && location.y <= BoundsNE_Longitude;

            return latitudeInBounds && longitudeInBounds;
        }

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
