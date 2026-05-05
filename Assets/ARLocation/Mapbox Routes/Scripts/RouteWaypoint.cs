using System.Collections;

namespace ARLocation.MapboxRoutes
{
    public enum RouteWaypointType
    {
        UserLocation,
        Location,
        Query
    };

    [System.Serializable]
    public class RouteWaypoint
    {
        public RouteWaypointType Type;
        public Location Location = new Location();
        public string Query;

        public override string ToString()
        {
            return "RouteWaypoint{ \n" +
                $"Type = {Type}\n" +
                $"Location = {Location}\n" +
                $"Query = {Query}\n" +
                "}";
        }
    }

    public class RouteWaypointResolveLocation
    {
        public Location result;
        public bool IsError;
        public string ErrorMessage;

        private RouteWaypoint w;
        private MapboxApi api;

        public RouteWaypointResolveLocation(MapboxApi mapboxApi, RouteWaypoint waypoint)
        {
            w = waypoint;
            api = mapboxApi;
        }

        public IEnumerator Resolve()
        {
            switch (w.Type)
            {
                case RouteWaypointType.Location:
                    result = w.Location;
                    IsError = false;
                    ErrorMessage = null;
                    yield break;

                case RouteWaypointType.UserLocation:
                    var currentLoc = ARLocationProvider.Instance.CurrentLocation.ToLocation();
                    
                    // Validate that the location has valid coordinates
                    if (currentLoc == null || (currentLoc.Latitude == 0 && currentLoc.Longitude == 0) ||
                        currentLoc.Latitude < -90 || currentLoc.Latitude > 90 ||
                        currentLoc.Longitude < -180 || currentLoc.Longitude > 180)
                    {
                        result = null;
                        IsError = true;
                        ErrorMessage = "User location not available or invalid. GPS coordinates must be within valid ranges (Lat: -90 to 90, Lon: -180 to 180).";
                    }
                    else
                    {
                        result = currentLoc;
                        IsError = false;
                        ErrorMessage = null;
                    }
                    yield break;

                case RouteWaypointType.Query:
                    yield return api.QueryLocal(w.Query);

                    if (api.ErrorMessage != null)
                    {
                        result = null;
                        IsError = true;
                        ErrorMessage = api.ErrorMessage;
                    }
                    else
                    {
                        result = api.QueryLocalResult.features[0].geometry.coordinates[0];
                        IsError = false;
                        ErrorMessage = null;
                    }

                    yield break;
            }
        }
    }
}
