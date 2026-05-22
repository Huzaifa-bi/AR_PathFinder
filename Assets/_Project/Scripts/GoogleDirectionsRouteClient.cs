using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ARLocation;
using ARLocation.MapboxRoutes;
using UnityEngine;
using UnityEngine.Networking;
using ARLocation.Vendor.SimpleJSON;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>
    /// Google Routes API (computeRoutes) → <see cref="RouteResponse"/> for the AR nav pipeline.
    /// Legacy Directions API is not enabled on new Google Cloud projects.
    /// </summary>
    public static class GoogleDirectionsRouteClient
    {
        public const string ComputeRoutesUrl =
            "https://routes.googleapis.com/directions/v2:computeRoutes";

        const string FieldMask =
            "routes.distanceMeters,routes.polyline.encodedPolyline," +
            "routes.legs.distanceMeters,routes.legs.steps.distanceMeters," +
            "routes.legs.steps.polyline.encodedPolyline," +
            "routes.legs.steps.navigationInstruction," +
            "routes.legs.steps.endLocation.latLng";

        public static bool IsApiKeyConfigured =>
            !string.IsNullOrWhiteSpace(PIEASConfig.GoogleMapsApiKey);

        public static IEnumerator LoadRoute(
            Location from,
            Location to,
            bool requestAlternativeRoutes,
            Action<string, RouteResponse> callback)
        {
            if (!IsApiKeyConfigured)
            {
                callback?.Invoke(
                    "Google Routes: add GoogleMapsApiKey to Assets/StreamingAssets/Secrets.json",
                    null);
                yield break;
            }

            if (from == null || to == null)
            {
                callback?.Invoke("Google Routes: missing origin or destination.", null);
                yield break;
            }

            string jsonBody = BuildRequestJson(from, to, requestAlternativeRoutes);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            using (var req = new UnityWebRequest(ComputeRoutesUrl, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 25;
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Goog-Api-Key", PIEASConfig.GoogleMapsApiKey);
                req.SetRequestHeader("X-Goog-FieldMask", FieldMask);

                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                string body = req.downloadHandler?.text;
                if (!ok)
                {
                    string detail = TryExtractApiError(body);
                    callback?.Invoke(
                        string.IsNullOrEmpty(detail)
                            ? $"Google Routes HTTP error: {req.error}"
                            : detail,
                        null);
                    yield break;
                }

                if (string.IsNullOrEmpty(body))
                {
                    callback?.Invoke("Google Routes: empty response.", null);
                    yield break;
                }

                if (!TryParseResponse(body, out RouteResponse route, out string err))
                {
                    callback?.Invoke(err ?? "Google Routes: parse failed.", null);
                    yield break;
                }

                callback?.Invoke(null, route);
            }
        }

        static string BuildRequestJson(Location from, Location to, bool alternatives)
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Format(
                inv,
                "{{\"origin\":{{\"location\":{{\"latLng\":{{\"latitude\":{0},\"longitude\":{1}}}}}}}," +
                "\"destination\":{{\"location\":{{\"latLng\":{{\"latitude\":{2},\"longitude\":{3}}}}}}}," +
                "\"travelMode\":\"{4}\",\"computeAlternativeRoutes\":{5}," +
                "\"languageCode\":\"en-US\",\"units\":\"METRIC\"}}",
                from.Latitude,
                from.Longitude,
                to.Latitude,
                to.Longitude,
                PIEASConfig.GoogleRoutesTravelMode,
                alternatives ? "true" : "false");
        }

        static string TryExtractApiError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var root = JSON.Parse(json);
                var err = root["error"];
                if (err == null) return null;
                string msg = err["message"]?.Value;
                string status = err["status"]?.Value;
                if (!string.IsNullOrEmpty(msg) && !string.IsNullOrEmpty(status))
                    return $"{status}: {msg}";
                return msg ?? status;
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryParseResponse(string json, out RouteResponse response, out string error)
        {
            response = null;
            error = null;

            try
            {
                var root = JSON.Parse(json);

                string apiErr = TryExtractApiError(json);
                if (!string.IsNullOrEmpty(apiErr))
                {
                    error = apiErr;
                    return false;
                }

                var routesArr = root["routes"]?.AsArray;
                if (routesArr == null || routesArr.Count == 0)
                {
                    error = "Google Routes: no routes returned.";
                    return false;
                }

                response = new RouteResponse
                {
                    Code = "OK",
                    routes = new List<Route>(),
                    waypoints = new List<Waypoint>()
                };

                for (int r = 0; r < routesArr.Count; r++)
                    response.routes.Add(ParseRoutesApiRoute(routesArr[r]));

                return response.routes.Count > 0 &&
                       response.routes[0].geometry?.coordinates?.Count >= 2;
            }
            catch (Exception ex)
            {
                error = $"Google Routes parse error: {ex.Message}";
                return false;
            }
        }

        static Route ParseRoutesApiRoute(JSONNode node)
        {
            var route = ScriptableObject.CreateInstance<Route>();
            route.legs = new List<Route.RouteLeg>();
            route.distance = node["distanceMeters"]?.AsFloat ?? 0f;

            var legsArr = node["legs"]?.AsArray;
            if (legsArr != null)
            {
                for (int i = 0; i < legsArr.Count; i++)
                    route.legs.Add(ParseRoutesApiLeg(legsArr[i]));
            }

            string overview = node["polyline"]?["encodedPolyline"]?.Value;
            route.geometry = new Route.Geometry
            {
                type = "LineString",
                coordinates = DecodePolyline(overview)
            };

            return route;
        }

        static Route.RouteLeg ParseRoutesApiLeg(JSONNode legNode)
        {
            var leg = new Route.RouteLeg
            {
                distance = legNode["distanceMeters"]?.AsFloat ?? 0f,
                steps = new List<Route.Step>()
            };

            var stepsArr = legNode["steps"]?.AsArray;
            if (stepsArr == null) return leg;

            for (int i = 0; i < stepsArr.Count; i++)
                leg.steps.Add(ParseRoutesApiStep(stepsArr[i]));

            return leg;
        }

        static Route.Step ParseRoutesApiStep(JSONNode stepNode)
        {
            var nav = stepNode["navigationInstruction"];
            string instruction = StripHtml(nav?["instructions"]?.Value);
            string maneuver = nav?["maneuver"]?.Value ?? "";

            var end = stepNode["endLocation"]?["latLng"];
            double lat = end?["latitude"]?.AsDouble ?? 0;
            double lng = end?["longitude"]?.AsDouble ?? 0;

            return new Route.Step
            {
                distance = stepNode["distanceMeters"]?.AsFloat ?? 0f,
                name = instruction,
                geometry = new Route.Geometry
                {
                    type = "LineString",
                    coordinates = DecodePolyline(stepNode["polyline"]?["encodedPolyline"]?.Value)
                },
                maneuver = new Route.Maneuver
                {
                    type = MapManeuverType(maneuver),
                    instruction = instruction,
                    location = new Location(lat, lng, 0),
                    bearing_before = 0,
                    bearing_after = 0
                }
            };
        }

        static string MapManeuverType(string googleManeuver)
        {
            if (string.IsNullOrEmpty(googleManeuver)) return "straight";
            googleManeuver = googleManeuver.ToLowerInvariant();
            if (googleManeuver.Contains("left")) return "left";
            if (googleManeuver.Contains("right")) return "right";
            if (googleManeuver.Contains("uturn") || googleManeuver.Contains("u-turn")) return "uturn";
            if (googleManeuver.Contains("roundabout")) return "roundabout";
            if (googleManeuver.Contains("merge")) return "merge";
            if (googleManeuver.Contains("fork")) return "fork";
            if (googleManeuver.Contains("arrive") || googleManeuver.Contains("destination")) return "arrive";
            if (googleManeuver.Contains("depart") || googleManeuver.Contains("start")) return "depart";
            return "straight";
        }

        static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            return Regex.Replace(html, "<.*?>", string.Empty).Trim();
        }

        /// <summary>Google encoded polyline → WGS84 locations.</summary>
        public static List<Location> DecodePolyline(string encoded)
        {
            var path = new List<Location>();
            if (string.IsNullOrEmpty(encoded)) return path;

            int index = 0;
            int len = encoded.Length;
            int lat = 0;
            int lng = 0;

            while (index < len)
            {
                int b;
                int shift = 0;
                int result = 0;
                do
                {
                    b = encoded[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20 && index < len);

                int dlat = (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
                lat += dlat;

                shift = 0;
                result = 0;
                do
                {
                    b = encoded[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20 && index < len);

                int dlng = (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
                lng += dlng;

                path.Add(new Location(lat / 1e5, lng / 1e5, 0));
            }

            return path;
        }

        /// <summary>Legacy name — uses <see cref="PIEASConfig.ActiveTravelProfile"/>.</summary>
        public static IEnumerator LoadWalkingRoute(
            Location from, Location to, bool requestAlternativeRoutes, Action<string, RouteResponse> callback) =>
            LoadRoute(from, to, requestAlternativeRoutes, callback);
    }
}
