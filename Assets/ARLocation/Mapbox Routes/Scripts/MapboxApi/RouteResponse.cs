using System;
using System.Collections.Generic;

namespace ARLocation.MapboxRoutes
{
    using Vendor.SimpleJSON;

    [Serializable]
    public class RouteResponse
    {
        public string Code;
        public List<Route> routes;
        public List<Waypoint> waypoints;

        public override string ToString()
        {
            string result = "";

            result += "RouteResponse{ routes = [";

            foreach (var route in routes)
            {
                result += route + ", ";
            }

            result += "], waypoints = [";

            foreach (var w in waypoints)
            {
                result += w + ", ";
            }

            result += $"Code = {Code}, ";

            result += "]}";

            return result;
        }
        
        public static RouteResponse Parse(string json)
        {
            return Parse(JSON.Parse(json));
        }

        public static RouteResponse Parse(JSONNode node)
        {
            var result = new RouteResponse();
            result.routes = new List<Route>();
            result.waypoints = new List<Waypoint>();

            result.Code = node["code"].Value;

            var arr = node["routes"].AsArray;

            for (int i = 0; i < arr.Count; i++)
            {
                result.routes.Add(Route.Parse(arr[i]));
            }

            arr = node["waypoints"].AsArray;

            for (int i = 0; i < arr.Count; i++)
            {
                result.waypoints.Add(Waypoint.Parse(arr[i]));
            }

            return result;
        }

        /// <summary>
        /// When the Directions API returns multiple routes (alternatives=true), keep only the shortest by <see cref="Route.distance"/>.
        /// </summary>
        public void KeepShortestRouteOnly()
        {
            if (routes == null || routes.Count <= 1) return;

            Route best = routes[0];
            float bestDist = best.distance;
            for (int i = 1; i < routes.Count; i++)
            {
                if (routes[i] != null && routes[i].distance < bestDist)
                {
                    bestDist = routes[i].distance;
                    best = routes[i];
                }
            }

            routes.Clear();
            routes.Add(best);
        }
    }

    [Serializable]
    public class Waypoint
    {
        public string name;
        public Location location;

        public override string ToString()
        {
            return $"Waypoint{{ name = {name}, location = {location} }}";
        }

        public static Waypoint Parse(JSONNode node)
        {
            var result = new Waypoint();

            result.name = node["name"];
            result.location = new Location(node["location"][1], node["location"][0], 0);

            return result;
        }
    }


}
