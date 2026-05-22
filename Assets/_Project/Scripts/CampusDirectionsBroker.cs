using System;
using System.Collections;
using UnityEngine;
using ARLocation;
using ARLocation.MapboxRoutes;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>
    /// Single entry for route requests (Google or Mapbox, profile from <see cref="PIEASConfig.ActiveTravelProfile"/>).
    /// </summary>
    public static class CampusDirectionsBroker
    {
        /// <summary>
        /// Loads a walking route between two resolved locations (same contract as <see cref="RouteLoader.LoadRoute"/> callback form).
        /// </summary>
        public static IEnumerator LoadWalkingRoute(
            string mapboxToken,
            RouteWaypoint start,
            RouteWaypoint end,
            bool requestAlternativeRoutes,
            bool verbose,
            Action<string, RouteResponse> callback)
        {
            if (PIEASConfig.ActiveDirectionsProvider == PIEASConfig.DirectionsProvider.GoogleDirections)
            {
                // Resolve waypoints still use Mapbox geocoding in RouteLoader; for Google-only later,
                // add a parallel resolver or Places API.
                var api = new MapboxApi(mapboxToken);
                var startResolver = new RouteWaypointResolveLocation(api, start);
                yield return startResolver.Resolve();
                if (startResolver.IsError)
                {
                    callback?.Invoke(startResolver.ErrorMessage, null);
                    yield break;
                }

                var endResolver = new RouteWaypointResolveLocation(api, end);
                yield return endResolver.Resolve();
                if (endResolver.IsError)
                {
                    callback?.Invoke(endResolver.ErrorMessage, null);
                    yield break;
                }

                yield return GoogleDirectionsRouteClient.LoadRoute(
                    startResolver.result,
                    endResolver.result,
                    requestAlternativeRoutes,
                    callback);
                yield break;
            }

            var mapboxApi = new MapboxApi(mapboxToken)
            {
                DirectionsProfile = PIEASConfig.MapboxDirectionsProfile
            };
            var loader = new RouteLoader(mapboxApi, verbose);
            yield return loader.LoadRoute(start, end, requestAlternativeRoutes);
            callback?.Invoke(loader.Error, loader.Result);
        }
    }
}
