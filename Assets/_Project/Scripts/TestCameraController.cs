using UnityEngine;
using ARLocation;
using Mapbox.Utils;
using Mapbox.Unity.Utilities;

// Script to handle test user indicator movement with WASD and destination detection
// This script moves a child object that represents the user (like a dummy arrow)
// while MapCamera stays as the view camera
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class TestCameraController : MonoBehaviour
    {
        [Header("Starting Location (for testing)")]
        [SerializeField] private double startingLatitude = PIEASConfig.CenterLatitude;   // Default: PIEAS Campus
        [SerializeField] private double startingLongitude = PIEASConfig.CenterLongitude;  // PIEAS Campus
        [SerializeField] private bool useGPSCoordinates = false;  // Toggle between GPS and world space
        [SerializeField] private Vector3 startingWorldPosition = Vector3.zero;  // Direct Unity coordinates - set to origin

        [Header("Camera Following")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(0, 5, -10);  // Camera position relative to player
        [SerializeField] private bool smoothFollow = true;
        [SerializeField] private float followSpeed = 5f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private Transform userIndicator;  // Independent player object representing user
        [SerializeField] private bool useWorldSpace = true;

        [Header("Destination Detection")]
        [SerializeField] private float destinationReachDistance = 5f;  // meters
        [SerializeField] private MenuController menuController;
        [SerializeField] private Mapbox.Unity.Map.AbstractMap map;

        private Vector3 userIndicatorStartPos;
        private bool hasReachedDestination = false;
        private float mapUpdateTimer = 0f;
        private const float MAP_UPDATE_INTERVAL = 1.0f; // Only update map once per second

        void Start()
        {
            if (menuController == null)
            {
                menuController = FindObjectOfType<MenuController>();
            }

            if (map == null)
            {
                map = FindObjectOfType<Mapbox.Unity.Map.AbstractMap>();
            }

            // Find the user indicator (should be an independent object in scene, not a child)
            if (userIndicator == null)
            {
                // Try to find it by name or type
                userIndicator = FindObjectOfType<Transform>();
                if (userIndicator != null && userIndicator.name.Contains("User"))
                {
                    Debug.Log($"[TestCameraController] Found user indicator: {userIndicator.name}");
                }
                else
                {
                    // If still not found, try to find the first child that's not the camera
                    foreach (Transform child in transform.parent != null ? transform.parent : transform)
                    {
                        if (child != transform)
                        {
                            userIndicator = child;
                            Debug.Log($"[TestCameraController] Found independent object: {userIndicator.name}");
                            break;
                        }
                    }
                }

                if (userIndicator == null)
                {
                    Debug.LogError("[TestCameraController] Could not find user indicator object! Please assign it manually in Inspector or ensure it exists in scene.");
                    this.enabled = false;
                    return;
                }
            }
            
            Debug.Log($"[TestCameraController] Using user indicator: '{userIndicator.name}' at position {userIndicator.position}");

            // Position the user indicator at the current testing location (0, 0, 0 for now)
            PositionAtCurrentLocation();
            
            // FORCE position to origin explicitly after positioning
            if (userIndicator != null)
            {
                userIndicator.position = Vector3.zero;
                Debug.Log($"[TestCameraController] FORCED user indicator '{userIndicator.name}' to origin (0,0,0). Final position: {userIndicator.position}");
            }

            userIndicatorStartPos = userIndicator.position;
            hasReachedDestination = false;
        }

        private void PositionAtCurrentLocation()
        {
            if (userIndicator == null)
            {
                Debug.LogError("[TestCameraController] userIndicator is null!");
                return;
            }

            try
            {
                // Set to origin (0, 0, 0) in world space
                Vector3 targetPosition = Vector3.zero;
                
                // Force position to origin - CRITICAL
                userIndicator.position = targetPosition;
                
                Debug.Log($"[TestCameraController] User indicator '{userIndicator.name}' positioned at {userIndicator.position}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TestCameraController] ERROR positioning user: {e.Message}");
            }
        }



        void Update()
        {
            // Handle GPS and Compass Movement
            HandleGPSMovement();

            // DO NOT update camera position in AR. The AR Session handles device tracking natively.
            // UpdateCameraPosition(); // <-- Hijacks AR Camera

            // Update map position based on user movement
            if (userIndicator != null && map != null)
            {
                UpdateMapPosition();
            }

            // Check destination arrival
            CheckDestinationArrival();
        }

        private void HandleGPSMovement()
        {
            if (userIndicator == null) return;
            if (ARLocationProvider.Instance == null || !ARLocationProvider.Instance.IsEnabled) return;

            // 1. POSITION (GPS to World on Map)
            var loc = ARLocationProvider.Instance.CurrentLocation.ToLocation();
            
            // Convert GPS to Mapbox world space position
            var targetPos = map.GeoToWorldPosition(new Mapbox.Utils.Vector2d(loc.Latitude, loc.Longitude), true);
            
            // Interpolate for smooth movement instead of snapping
            userIndicator.position = Vector3.Lerp(userIndicator.position, targetPos, moveSpeed * Time.deltaTime);

            // 2. ROTATION (Compass)
            // Enable compass if not already
            if (!Input.compass.enabled) Input.compass.enabled = true;

            // Get true heading and construct the target rotation (around Y axis)
            float trueHeading = Input.compass.trueHeading;
            Quaternion targetRotation = Quaternion.Euler(0, trueHeading, 0);

            // Interpolate rotation for smooth arrow turning
            userIndicator.rotation = Quaternion.Slerp(userIndicator.rotation, targetRotation, moveSpeed * Time.deltaTime);
        }


        private void UpdateMapPosition()
        {
            if (userIndicator == null || map == null) return;

            // Throttle map updates to avoid API spam (was called every frame!)
            mapUpdateTimer += Time.deltaTime;
            if (mapUpdateTimer < MAP_UPDATE_INTERVAL) return;
            mapUpdateTimer = 0f;

            // Update map position based on user movement
            if (menuController != null && menuController.Map != null)
            {
                menuController.Map.UpdateMap();
            }
        }

        private void UpdateCameraPosition()
        {
            if (userIndicator == null) return;

            // Calculate where camera should be (player position + offset)
            Vector3 targetCameraPos = userIndicator.position + cameraOffset;

            // Smooth follow or instant follow
            if (smoothFollow)
            {
                transform.position = Vector3.Lerp(transform.position, targetCameraPos, followSpeed * Time.deltaTime);
            }
            else
            {
                transform.position = targetCameraPos;
            }

            // Optional: Make camera look at player
            // transform.LookAt(userIndicator.position);
        }

        private void CheckDestinationArrival()
        {
            if (menuController == null || userIndicator == null || hasReachedDestination)
            {
                return;
            }

            if (menuController.MapboxRoute != null && menuController.MapboxRoute.NumberOfSteps > 0)
            {
                var routeSettings = menuController.MapboxRoute.Settings.RouteSettings;
                if (routeSettings != null)
                {
                    // The target destination is typically the end of the route
                    Location destinationLoc;
                    
                    if (routeSettings.To.Type == RouteWaypointType.Location) 
                    {
                        destinationLoc = routeSettings.To.Location;
                    } 
                    else if (menuController.MapboxRoute.Settings.RouteSettings.CustomRoute != null && 
                             menuController.MapboxRoute.Settings.RouteSettings.CustomRoute.Points.Count > 0)
                    {
                        var points = menuController.MapboxRoute.Settings.RouteSettings.CustomRoute.Points;
                        destinationLoc = points[points.Count - 1].Location; // last point
                    }
                    else
                    {
                        // Fallback checking distances against Mapbox routes nodes internally?
                        // If we don't have a specific `To` location, we can't do simple distance math
                        return;
                    }

                    // Calculate distance using ARLocation's accurate horizontal math
                    var currentLocation = ARLocationProvider.Instance.CurrentLocation.ToLocation();
                    double distance = Location.HorizontalDistance(currentLocation, destinationLoc);

                    if (distance <= destinationReachDistance)
                    {
                        hasReachedDestination = true;
                        Debug.Log($"[TestCameraController] ✅ DESTINATION REACHED! Distance: {distance:F2}m");
                        
                        // Notify MenuController
                        menuController.CancelRouteDueToArrival();
                    }
                }
            }
        }

        private void CheckDistanceToCurrentTarget(Vector3 userPosition)
        {
            // Distance tracking happens through MapboxRoute's internal system
        }

        // Call this from MenuController when updating route
        public void SetCurrentTarget(Vector3 targetPosition)
        {
            if (userIndicator == null) return;

            float distanceToTarget = Vector3.Distance(userIndicator.position, targetPosition);

            if (distanceToTarget <= destinationReachDistance && !hasReachedDestination)
            {
                hasReachedDestination = true;
                Debug.Log($"[TestCameraController] ✅ DESTINATION REACHED! Distance: {distanceToTarget:F2}m");
            }
            else if (distanceToTarget > destinationReachDistance)
            {
                hasReachedDestination = false;
            }
        }

        // Reset destination flag for new route
        public void ResetDestinationFlag()
        {
            hasReachedDestination = false;
        }

        // Get current user position
        public Vector3 GetCurrentPosition()
        {
            return userIndicator != null ? userIndicator.position : Vector3.zero;
        }

        // Setter to change starting location dynamically for testing (GPS coordinates)
        public void SetStartingLocation(double latitude, double longitude)
        {
            startingLatitude = latitude;
            startingLongitude = longitude;
            useGPSCoordinates = true;
            PositionAtCurrentLocation();
        }

        // Setter to change starting location using world coordinates
        public void SetStartingLocationWorld(Vector3 worldPosition)
        {
            startingWorldPosition = Vector3.zero;  // Always use origin
            useGPSCoordinates = false;
            PositionAtCurrentLocation();
        }
    }
}
