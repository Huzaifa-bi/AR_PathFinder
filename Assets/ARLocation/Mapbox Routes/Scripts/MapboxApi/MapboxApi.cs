using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Globalization;

namespace ARLocation.MapboxRoutes
{
    using Vendor.SimpleJSON;

    [System.Serializable]
    public class MapboxApi
    {
        public string AccessToken;

        private RouteResponse queryRouteResult;
        public RouteResponse QueryRouteResult => queryRouteResult;

        private GeocodingResponse queryLocalResult;
        public GeocodingResponse QueryLocalResult => queryLocalResult;

        public string errorMessage;
        public string ErrorMessage => errorMessage;

        public MapboxApi(string token)
        {
            AccessToken = token;
        }

        public IEnumerator QueryLocal(string text, bool verbose = false)
        {
            var term = text;
            // Scope search to PIEAS/Islamabad region with proximity bias and bounding box
            var proximity = "73.2650,33.6560"; // PIEAS campus center (lon,lat)
            var bbox = "73.00,33.50,73.50,33.85"; // Broader Islamabad region bounding box
            var url = Uri.EscapeUriString($"https://api.mapbox.com/geocoding/v5/mapbox.places/{term}.json?proximity={proximity}&bbox={bbox}&access_token={AccessToken}");

            errorMessage = null;
            queryLocalResult = null;

            if (verbose)
            {
                Debug.Log($"[MapboxApi#QueryLocal]: {url}");
            }

            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (Utils.Misc.WebRequestResultIsError(req))
                {
                    if (verbose)
                    {
                        Debug.LogError("[MapboxApi#QueryLocal]: Error -> " + req.error);
                    }

                    errorMessage = req.error;
                }
                else
                {
                    if (req.responseCode != 200)
                    {
                        if (verbose)
                        {
                            Debug.LogError("[MapboxApi#QueryLocal]: Error -> " + req.downloadHandler.text);
                            var node = JSON.Parse(req.downloadHandler.text);
                            errorMessage = node["message"].Value; //req.downloadHandler.text;
                            queryLocalResult = null;
                        }
                    }
                    else
                    {
                        if (verbose)
                        {
                            Debug.Log("[MapboxApi#QueryLocal]: Success -> " + req.downloadHandler.text);
                        }

                        queryLocalResult = GeocodingResponse.Parse(req.downloadHandler.text);
                    }
                }
            }
        }

        public IEnumerator QueryRoute(Location from, Location to, bool alternatives = false, bool verbose = false)
        {
            string alt = alternatives ? "true" : "false";

            var fromLat = from.Latitude.ToString(CultureInfo.InvariantCulture);
            var fromLon = from.Longitude.ToString(CultureInfo.InvariantCulture);
            var toLat = to.Latitude.ToString(CultureInfo.InvariantCulture);
            var toLon = to.Longitude.ToString(CultureInfo.InvariantCulture);
            
            // Validate coordinates are within valid ranges
            if (!IsValidCoordinate(from.Latitude, from.Longitude) || !IsValidCoordinate(to.Latitude, to.Longitude))
            {
                errorMessage = "Invalid coordinates: latitude must be between -90 and 90, longitude between -180 and 180";
                queryRouteResult = null;
                
                if (verbose)
                {
                    Debug.LogError($"[MapboxApi#QueryRoute]: Invalid coordinates - From ({from.Latitude}, {from.Longitude}) to ({to.Latitude}, {to.Longitude})");
                }
                
                yield break;
            }
            
            string url = $"https://api.mapbox.com/directions/v5/mapbox/walking/{fromLon},{fromLat};{toLon},{toLat}?alternatives={alt}&geometries=geojson&steps=true&access_token={AccessToken}";
            
            errorMessage = null;
            queryRouteResult = null;

            if (verbose)
            {
                Debug.Log($"[MapboxApi#QueryRoute]: {url}");
                Debug.Log($"[MapboxApi#QueryRoute]: From ({fromLat}, {fromLon}) to ({toLat}, {toLon})");
            }

            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (Utils.Misc.WebRequestResultIsError(req))
                {
                    if (verbose)
                    {
                        Debug.LogError("[MapboxApi#QueryRoute]: Network Error -> " + req.error);
                    }

                    errorMessage = req.error;
                }
                else
                {
                    if (req.responseCode != 200)
                    {
                        if (verbose)
                        {
                            Debug.LogError($"[MapboxApi#QueryRoute]: HTTP {req.responseCode} Error -> {req.downloadHandler.text}");
                        }

                        // Try to parse error message from response
                        try
                        {
                            var node = JSON.Parse(req.downloadHandler.text);
                            errorMessage = $"HTTP {req.responseCode}: {node["message"].Value}";
                        }
                        catch
                        {
                            errorMessage = $"HTTP {req.responseCode}: {req.downloadHandler.text}";
                        }
                        queryRouteResult = null;
                    }
                    else
                    {
                        if (verbose)
                        {
                            Debug.Log("[MapboxApi#QueryRoute]: Success -> " + req.downloadHandler.text);
                            Debug.Log("[MapboxApi#QueryRoute]: Response Code -> " + req.responseCode);
                        }

                        queryRouteResult = RouteResponse.Parse(req.downloadHandler.text);

                        if (queryRouteResult.Code != "Ok")
                        {
                            errorMessage = $"Mapbox API Error: {queryRouteResult.Code}";
                            queryRouteResult = null;
                        }
                        else
                        {
                            if (verbose)
                            {
                                Debug.Log("[MapboxApi#QueryRoute]: Route parsed successfully");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates that latitude and longitude are within valid ranges.
        /// Latitude: -90 to 90
        /// Longitude: -180 to 180
        /// </summary>
        private bool IsValidCoordinate(double latitude, double longitude)
        {
            return latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;
        }
    }
}
