using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using System.Reflection;
using ARLocation.Session;

//This script, `MenuController`, manages the user interface and interactions for a sample project related to ARLocation and Mapbox routes. 
//This script orchestrates various functionalities such as searching for locations, displaying search results, initiating routes, updating map visuals, and managing UI transitions between the search menu and route view.
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class MenuController : MonoBehaviour
    {
        public enum LineType
        {
            Route,
            NextTarget
        }
//   - Various fields are serialized for easy access and configuration in the Unity Editor. These include references to objects like the AR session, cameras, route renderers, materials, etc.
        // Mapbox API token for PIEAS Campus Navigation - centralized
        public string MapboxToken = PIEASConfig.MapboxToken;
        
        // AR Foundation components (platform-agnostic) - ENABLED FOR PRODUCTION
        // public ARSessionOrigin ARSessionOrigin;
        // public ARSession ARSession;
        public ARCameraManager ARCameraManager;
        public ARPlaneManager ARPlaneManager;
        public ARRaycastManager ARRaycastManager;
        
        // Game objects managed by MenuController
        public GameObject RouteContainer;
        public Camera Camera;
        public Camera MapboxMapCamera;
        public MapboxRoute MapboxRoute;
        public AbstractRouteRenderer RoutePathRenderer;
        public AbstractRouteRenderer NextTargetPathRenderer;
        public Texture RenderTexture;
        public Mapbox.Unity.Map.AbstractMap Map;
        [Range(100, 800)]
        public int MapSize = 400;
        public DirectionsFactory DirectionsFactory;
        public int MinimapLayer;
        public Material MinimapLineMaterial;
        public float BaseLineWidth = 3f;
        public float MinimapStepSize = 0.5f;

        // ── UI + Audio ──
        private NavigationAudioSystem _audioSystem;
        private string               _destinationName = "Destination";
        private Vector2              _guiScrollPos;

        private AbstractRouteRenderer currentPathRenderer => s.LineType == LineType.Route ? RoutePathRenderer : NextTargetPathRenderer;

        public LineType PathRendererType
        { //   - Defines an enum `LineType` to distinguish between route lines and lines for the next target.
            get => s.LineType;
            set
            {
                if (value != s.LineType)
                {
                    currentPathRenderer.enabled = false;
                    s.LineType = value;
                    currentPathRenderer.enabled = true;

                    if (s.View == View.Route)
                    {
                        MapboxRoute.RoutePathRenderer = currentPathRenderer;
                    }
                }
            }
        }

        enum View
        {
            SearchMenu,
            Route,
        }

        [System.Serializable]
        private class State
        { //- Contains fields to hold the current state of the menu, including the query text, search results, view mode, selected destination, line type, and error messages.
            public string QueryText = "";
            public List<GeocodingFeature> Results = new List<GeocodingFeature>();
            public View View = View.SearchMenu;
            public Location destination;
            public LineType LineType = LineType.NextTarget;
            public string ErrorMessage;
            public string SuccessMessage; // Added for destination arrival
        }

        private State s = new State();

        private GUIStyle _textStyle;
        GUIStyle textStyle()
        {
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label);
                _textStyle.fontSize = 48;
                _textStyle.fontStyle = FontStyle.Bold;
            }

            return _textStyle;
        }

        private GUIStyle _textFieldStyle;
        GUIStyle textFieldStyle()
        {
            if (_textFieldStyle == null)
            {
                _textFieldStyle = new GUIStyle(GUI.skin.textField);
                _textFieldStyle.fontSize = 60;
            }
            return _textFieldStyle;
        }

        private GUIStyle _errorLabelStyle;
        GUIStyle errorLabelSytle()
        {
            if (_errorLabelStyle == null)
            {
                _errorLabelStyle = new GUIStyle(GUI.skin.label);
                _errorLabelStyle.fontSize = 24;
                _errorLabelStyle.fontStyle = FontStyle.Bold;
                _errorLabelStyle.normal.textColor = Color.red;
            }

            return _errorLabelStyle;
        }


        private GUIStyle _buttonStyle;
        GUIStyle buttonStyle()
        {
            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button);
                _buttonStyle.fontSize = 48;
            }

            return _buttonStyle;
        }        void Awake()
        {
            Debug.Log("[MenuController#Awake]: Initializing AR Restoration...");

            // 1. FORCE ANDROID PERMISSION REQUEST — Camera + Location
            if (Application.platform == RuntimePlatform.Android)
            {
                #if UNITY_ANDROID
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    Debug.Log("[MenuController#Awake]: Requesting Camera Permission.");
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                }
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                {
                    Debug.Log("[MenuController#Awake]: Requesting FineLocation Permission.");
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                }
                #endif
            }

            // 2. NUCLEAR CAMERA FIX: Forcefully find and disable EVERY camera that isn't AR
            StartCoroutine(NuclearCameraFixCoroutine());

            // 3. ACTIVATE AR SESSION (DELAYED ON ANDROID UNTIL PERMISSIONS)
            var arSession = FindObjectOfType<ARSession>(true);
            if (arSession != null)
            {
                #if UNITY_ANDROID
                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    arSession.gameObject.SetActive(true);
                    arSession.enabled = true;
                }
                else
                {
                    Debug.Log("[MenuController#Awake]: Delaying ARSession activation until Camera permission is granted.");
                }
                #else
                arSession.gameObject.SetActive(true);
                arSession.enabled = true;
                #endif
            }

            // 4. ACTIVATE AR SESSION ORIGIN
            var arSessionOrigin = FindObjectOfType<ARSessionOrigin>(true);
            if (arSessionOrigin != null)
            {
                arSessionOrigin.gameObject.SetActive(true);
                var arCam = arSessionOrigin.camera;
                if (arCam != null)
                {
                    // Mandatory: ARCameraManager handles the feed logic
                    if (arCam.GetComponent<ARCameraManager>() == null)
                    {
                        arCam.gameObject.AddComponent<ARCameraManager>();
                    }

                    // Mandatory: ARCameraBackground renders the feed
                    if (arCam.GetComponent<ARCameraBackground>() == null)
                    {
                        arCam.gameObject.AddComponent<ARCameraBackground>();
                    }
                    arCam.tag = "MainCamera";
                }
            }
        }

        private IEnumerator NuclearCameraFixCoroutine()
        {
            yield return new WaitForSeconds(0.5f);

            Camera arCamera = null;
            var origin = FindObjectOfType<ARSessionOrigin>(true);
            if (origin != null) arCamera = origin.camera;

            var allCameras = FindObjectsOfType<Camera>(true);
            foreach (var cam in allCameras)
            {
                if (cam != arCamera && cam.gameObject.name != "MapCamera")
                {
                    cam.enabled = false;
                    cam.gameObject.SetActive(false);
                }
            }

            if (arCamera != null)
            {
                arCamera.enabled = true;
                arCamera.gameObject.SetActive(true);
            }
        }

        private IEnumerator ARDiagnosticLoop()
        {
            Debug.Log("[ARDiagnostic]: Starting Diagnostic Loop...");
            yield return new WaitForSeconds(1.0f);
            
            while (true)
            {
                #if UNITY_ANDROID
                // 1. Check Camera Permission
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    Debug.Log("[ARDiagnostic]: Requesting Camera Permission...");
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                    yield return new WaitForSeconds(1.0f);
                }
                
                // 2. Check Location Permission (Required for ARLocation)
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                {
                    Debug.Log("[ARDiagnostic]: Requesting Location Permission...");
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                    yield return new WaitForSeconds(1.0f);
                }

                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    // 3. Ensure ARSession is active once permission is granted
                    var arSession = FindObjectOfType<ARSession>(true);
                    if (arSession != null && (!arSession.gameObject.activeSelf || !arSession.enabled))
                    {
                        Debug.Log("[ARDiagnostic]: Permissions granted. Activating ARSession.");
                        arSession.gameObject.SetActive(true);
                        arSession.enabled = true;
                        yield return new WaitForSeconds(0.5f);
                    }

                    var state = ARSession.state;
                    Debug.Log($"[ARDiagnostic]: Current AR State: {state}");

                    if (state == ARSessionState.None || state == ARSessionState.CheckingAvailability)
                    {
                        Debug.Log("[ARDiagnostic]: Checking AR Availability...");
                        yield return ARSession.CheckAvailability();
                    }
                    
                    if (ARSession.state == ARSessionState.NeedsInstall)
                    {
                        Debug.Log("[ARDiagnostic]: ARCore Needs Install/Update. Triggering Installation...");
                        yield return ARSession.Install();
                    }

                    if (ARSession.state == ARSessionState.Unsupported)
                    {
                        Debug.LogWarning("[ARDiagnostic]: Device reports AR Unsupported. Check: 1) Graphics API is OpenGLES3, 2) XR Plugin Management has ARCore enabled, 3) AR Session is active in scene.");
                    }
                }
                #endif
                yield return new WaitForSeconds(3.0f);
            }
        }

        void Start()
        { 
            StartCoroutine(ARDiagnosticLoop());

            // CRITICAL: Ensure mock location is set to PIEAS
            if (ARLocationProvider.Instance != null && ARLocationProvider.Instance.Provider is MockLocationProvider mockProvider)
            {
                if ((mockProvider.mockLocation.Latitude == 0 && mockProvider.mockLocation.Longitude == 0) ||
                    (mockProvider.mockLocation.Latitude == -24.499597 && mockProvider.mockLocation.Longitude == -47.868469))
                {
                    // Reset to PIEAS if not set or if it's the wrong coordinates
                    mockProvider.mockLocation = new Location
                    {
                        Latitude = 33.65598735240187,
                        Longitude = 73.2649697331715,
                        Altitude = 0
                    };
                    Debug.Log("[MenuController#Start]: Mock location reset to PIEAS Campus");
                }
            }
            
            // Find Map if not assigned in inspector
            if (Map == null)
            {
                Map = FindObjectOfType<Mapbox.Unity.Map.AbstractMap>();
                if (Map == null)
                {
                    Debug.LogError("[MenuController#Start]: Map (AbstractMap) not found in scene. Please assign it in the Inspector or ensure it exists in the scene.");
                    return;
                }
            }
            
            // Set default map center coordinates (Islamabad)
            // Delay the map update to ensure the map is fully initialized
            try
            {
                // Only set center if Map is initialized
                if (Map != null && Map.IsAccessTokenValid)
                {
                    // Set PIEAS Campus Center
                    Map.SetCenterLatitudeLongitude(PIEASConfig.CampusCenter);
                    Map.SetZoom(PIEASConfig.DefaultZoomLevel);
                    // Schedule update for next frame to ensure proper initialization
                    StartCoroutine(UpdateMapNextFrame());
                }
                else if (Map != null)
                {
                    Debug.LogWarning("[MenuController#Start]: Map is not fully initialized yet or access token is invalid. Map will use default coordinates.");
                    // Schedule update for next frame
                    StartCoroutine(UpdateMapNextFrame());
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MenuController#Start]: Error setting map center: {e.Message}");
            }
            
            // AR Foundation - ENABLED FOR PRODUCTION
            if (ARCameraManager == null)
            {
                ARCameraManager = FindObjectOfType<ARCameraManager>();
            }
            if (ARPlaneManager == null)
            {
                ARPlaneManager = FindObjectOfType<ARPlaneManager>();
            }
            if (ARRaycastManager == null)
            {
                ARRaycastManager = FindObjectOfType<ARRaycastManager>();
            }

            // Initialize route renderers
            NextTargetPathRenderer.enabled = false;
            RoutePathRenderer.enabled = false;

            // Subscribe to location provider
            if (ARLocationProvider.Instance != null)
            {
                ARLocationProvider.Instance.OnEnabled.AddListener(onLocationEnabled);
                Debug.Log("[MenuController#Start]: AR Location Provider connected");
            }
            else
            {
                Debug.LogWarning("[MenuController#Start]: ARLocationProvider.Instance is null - AR will not work");
            }
            
            // Subscribe to map update events if Map exists
            if (Map != null)
            {
                Map.OnUpdated += OnMapRedrawn;
            }
            
            Debug.Log("[MenuController#Start]: AR Foundation ENABLED - Production ready");

            // ── Initialize Audio System (hidden — not called yet) ─────────────
            _audioSystem = gameObject.GetComponent<NavigationAudioSystem>()
                        ?? gameObject.AddComponent<NavigationAudioSystem>();
        }

        /// <summary>
        /// Returns the campus location list from CampusLocations singleton,
        /// or a hardcoded fallback if it hasn't initialized yet.
        /// </summary>
        private List<CampusLocation> GetCampusLocationList()
        {
            if (CampusLocations.Instance != null)
                return CampusLocations.Instance.GetAllLocations();

            // Fallback — matches the same data in CampusLocations.InitializeDefaultLocations()
            return new List<CampusLocation>
            {
                new CampusLocation { Name = "C-block",                Description = "PIEAS C Block",                 Coordinates = new Mapbox.Utils.Vector2d(33.65578597201986, 73.26552018567683) },
                new CampusLocation { Name = "D-block",                Description = "PIEAS D Block",                 Coordinates = new Mapbox.Utils.Vector2d(33.65533195716392, 73.26561587673456) },
                new CampusLocation { Name = "PIEAS Central Library", Description = "Library",                        Coordinates = new Mapbox.Utils.Vector2d(33.6554567451093,  73.26708313965757) },
                new CampusLocation { Name = "Auditorium",            Description = "Inaam-ur-Rehman Auditorium",     Coordinates = new Mapbox.Utils.Vector2d(33.655887550014555,73.26772910917398) },
                new CampusLocation { Name = "DNE",                   Description = "Department Nuclear Engineering", Coordinates = new Mapbox.Utils.Vector2d(33.654431025749346,73.26334063974608) },
            };
        }

        /// <summary>
        /// Coroutine to update map on the next frame to ensure proper initialization
        /// </summary>
        private IEnumerator UpdateMapNextFrame()
        {
            yield return null; // Wait one frame
            
            if (Map != null)
            {
                try
                {
                    Map.UpdateMap();
                    Debug.Log("[MenuController#UpdateMapNextFrame]: Map updated successfully");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MenuController#UpdateMapNextFrame]: Error updating map: {e.Message}");
                }
            }
        }

        private void OnMapRedrawn()
        {
            // Debug.Log("OnMapRedrawn");
            if (currentResponse != null)
            {
                buildMinimapRoute(currentResponse);
            }
        }

        private void onLocationEnabled(Location location)
        {
            if (Map == null || location == null)
            {
                return;
            }

            try
            {
                Map.SetCenterLatitudeLongitude(new Mapbox.Utils.Vector2d(location.Latitude, location.Longitude));
                // Schedule update for next frame to avoid conflicts
                StartCoroutine(UpdateMapNextFrame());
                Debug.Log($"[MenuController#onLocationEnabled]: Map centered on location ({location.Latitude}, {location.Longitude})");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MenuController#onLocationEnabled]: Error updating map with location: {e.Message}");
            }
        }

        void OnEnable()
        {
            Debug.Log("MenuController enabled");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        { //   - Unsubscribes from events to prevent memory leaks.
            // ARLocationProvider.Instance.OnEnabled.RemoveListener(onLocationEnabled);
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        { //   - Handles scene loading events.
            Debug.Log($"Scene Loaded: {scene.name}");
        }

        void drawMap()
        { //- Draws the map on the GUI using a texture.
            if (Map == null || RenderTexture == null)
            {
                return;
            }

            var tw = RenderTexture.width;
            var th = RenderTexture.height;

            var scale = MapSize / th;
            var newWidth = scale * tw;
            var x = Screen.width / 2 - newWidth / 2;
            float border;
            if (x < 0)
            {
                border = -x;
            }
            else
            {
                border = 0;
            }


            GUI.DrawTexture(new Rect(x, Screen.height - MapSize, newWidth, MapSize), RenderTexture, ScaleMode.ScaleAndCrop);
            GUI.DrawTexture(new Rect(0, Screen.height - MapSize - 20, Screen.width, 20), separatorTexture, ScaleMode.StretchToFill, false);

            var newZoom = GUI.HorizontalSlider(new Rect(0, Screen.height - 60, Screen.width, 60), Map.Zoom, 10, 22);

            if (newZoom != Map.Zoom)
            {
                try
                {
                    Map.SetZoom(newZoom);
                    StartCoroutine(UpdateMapNextFrame());
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MenuController#drawMap]: Error updating zoom: {e.Message}");
                }
            }
        }

        void OnGUI()
        {
            if (s.View == View.Route)
            {
                // ── Navigation View: destination label + end button + map ──
                GUI.Label(new Rect(10, 10, Screen.width - 230, 60),
                    "\u2192 " + _destinationName, textStyle());

                if (GUI.Button(new Rect(Screen.width - 210, 10, 200, 60),
                    "End Nav", buttonStyle()))
                {
                    EndRoute();
                }

                drawMap();
                return;
            }

            // ── Search Menu View ──
            GUILayout.BeginVertical(new GUIStyle() { padding = new RectOffset(20, 20, 20, 20) });

            GUILayout.Label("AR PathFinder \u2014 PIEAS Campus", textStyle());

            // Search row
            GUILayout.BeginHorizontal(GUILayout.MaxHeight(100), GUILayout.MinHeight(100));
            s.QueryText = GUILayout.TextField(s.QueryText, textFieldStyle(),
                GUILayout.MinWidth(0.7f * Screen.width), GUILayout.MaxWidth(0.7f * Screen.width));

            if (GUILayout.Button("Search", buttonStyle(),
                GUILayout.MinWidth(0.2f * Screen.width), GUILayout.MaxWidth(0.2f * Screen.width)))
            {
                s.ErrorMessage = null;

                // Try local campus match first
                var campusLocs = GetCampusLocationList();
                string q = (s.QueryText ?? "").Trim().ToLowerInvariant();
                bool matched = false;
                if (!string.IsNullOrEmpty(q))
                {
                    for (int i = 0; i < campusLocs.Count; i++)
                    {
                        if (campusLocs[i].Name.ToLowerInvariant().Contains(q))
                        {
                            Debug.Log($"[MenuController]: Local match \u2014 '{campusLocs[i].Name}'");
                            _destinationName = campusLocs[i].Name;
                            StartRoute(new Location
                            {
                                Latitude  = campusLocs[i].Coordinates.x,
                                Longitude = campusLocs[i].Coordinates.y
                            });
                            matched = true;
                            break;
                        }
                    }
                }
                if (!matched)
                    StartCoroutine(search());
            }
            GUILayout.EndHorizontal();

            // Error / Success messages
            if (s.ErrorMessage != null)
                GUILayout.Label(s.ErrorMessage, errorLabelSytle());

            if (s.SuccessMessage != null)
            {
                var okStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 24, fontStyle = FontStyle.Bold };
                okStyle.normal.textColor = Color.green;
                GUILayout.Label(s.SuccessMessage, okStyle);
            }

            // Scrollable area for results + campus locations
            _guiScrollPos = GUILayout.BeginScrollView(_guiScrollPos);

            // Search results (from Mapbox API)
            if (s.Results != null && s.Results.Count > 0)
            {
                var secStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 32, fontStyle = FontStyle.Bold };
                GUILayout.Label("Search Results:", secStyle);
                foreach (var r in s.Results)
                {
                    var bs = new GUIStyle(buttonStyle())
                        { alignment = TextAnchor.MiddleLeft, fontSize = 24 };
                    bs.fixedHeight = 0.05f * Screen.height;
                    if (GUILayout.Button(r.place_name, bs))
                    {
                        _destinationName = r.place_name;
                        StartRoute(r.geometry.coordinates[0]);
                    }
                }
            }

            // Campus locations
            GUILayout.Space(10);
            var campusHeaderStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 32, fontStyle = FontStyle.Bold };
            GUILayout.Label("PIEAS Campus Locations", campusHeaderStyle);

            var locations = GetCampusLocationList();
            foreach (var loc in locations)
            {
                var bs = new GUIStyle(buttonStyle())
                    { alignment = TextAnchor.MiddleLeft, fontSize = 24 };
                bs.fixedHeight = 0.06f * Screen.height;
                if (GUILayout.Button(loc.Name + "  \u2014  " + loc.Description, bs))
                {
                    _destinationName = loc.Name;
                    StartRoute(new Location
                    {
                        Latitude  = loc.Coordinates.x,
                        Longitude = loc.Coordinates.y
                    });
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            drawMap();
        }

        private Texture2D _separatorTexture;
        private Texture2D separatorTexture
        {
            get
            {
                if (_separatorTexture == null)
                {
                    _separatorTexture = new Texture2D(1, 1);
                    _separatorTexture.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f));
                    _separatorTexture.Apply();
                }

                return _separatorTexture;
            }
        }

        public void StartRoute(Location dest)
        { //    - Initiates the route calculation based on the selected destination.
            s.destination = dest;
            
            Debug.Log($"[MenuController] Starting route to destination - Lat: {dest.Latitude}, Lon: {dest.Longitude}");

            // AR Foundation - Load route using current location
            if (ARLocationProvider.Instance?.IsEnabled ?? false)
            {
                loadRoute(ARLocationProvider.Instance.CurrentLocation.ToLocation());
            }
            else
            {
                // Use PIEASConfig fallback coordinates for consistency
                loadRoute(new Location { Latitude = PIEASConfig.CenterLatitude, Longitude = PIEASConfig.CenterLongitude });
            }
        }

        public void EndRoute()
        { //- Ends the route by returning to search menu.
            s.View = View.SearchMenu;
            if (RouteContainer != null) RouteContainer.SetActive(false);
            if (MapboxRoute != null && MapboxRoute.RoutePathRenderer != null) MapboxRoute.RoutePathRenderer.enabled = false;


            Debug.Log("[MenuController#EndRoute]: Returned to search menu");
        }

        public void CancelRouteDueToArrival()
        {
            EndRoute();
            s.SuccessMessage = $"🎉 You have arrived at {_destinationName}!";
            s.ErrorMessage = null;

            // _audioSystem?.SpeakArrived(_destinationName); // Uncomment to enable audio
            StartCoroutine(ClearSuccessMessage());
        }

        private IEnumerator ClearSuccessMessage()
        {
            yield return new WaitForSeconds(5f);
            s.SuccessMessage = null;
        }

        private void loadRoute(Location startLocationParam)
        { //    - Loads the route based on the provided destination and updates the map accordingly.
            if (s.destination == null) return;

            // Use the passed-in location as the start, with fallback validation
            Location startLocation = startLocationParam;
            
            // Use fallback if location is invalid, zero, or too far from destination
            bool useFallback = startLocation == null || 
                               (startLocation.Latitude == 0 && startLocation.Longitude == 0) || 
                               !IsValidCoordinate(startLocation) ||
                               IsTooFarFromDestination(startLocation, s.destination);
            
            if (useFallback)
            {
                startLocation = new Location { Latitude = PIEASConfig.CenterLatitude, Longitude = PIEASConfig.CenterLongitude };
                Debug.LogWarning("[MenuController#loadRoute]: User location not available or invalid, using PIEAS center fallback coordinates");
            }
            
            // Validate destination coordinates
            if (!IsValidCoordinate(s.destination))
            {
                s.ErrorMessage = $"Invalid destination coordinates: Lat={s.destination.Latitude}, Lon={s.destination.Longitude}";
                Debug.LogError($"[MenuController#loadRoute]: {s.ErrorMessage}");
                return;
            }
            
            Debug.Log($"[MenuController#loadRoute]: Starting route from ({startLocation.Latitude}, {startLocation.Longitude}) to ({s.destination.Latitude}, {s.destination.Longitude})");

            // ============================================================
            //  TRY CUSTOM CAMPUS PATHFINDER FIRST
            //  Uses actual campus walkways instead of Mapbox road data
            // ============================================================
            var startCoord = new Mapbox.Utils.Vector2d(startLocation.Latitude, startLocation.Longitude);
            var destCoord = new Mapbox.Utils.Vector2d(s.destination.Latitude, s.destination.Longitude);
            
            if (PIEASConfig.IsWithinCampusBounds(startCoord) && PIEASConfig.IsWithinCampusBounds(destCoord))
            {
                Debug.Log("[MenuController#loadRoute]: Both points within campus — using custom campus pathfinder");
                
                var path = CampusPathGraph.Instance.FindPath(
                    startLocation.Latitude, startLocation.Longitude,
                    s.destination.Latitude, s.destination.Longitude);
                
                if (path != null && path.Count >= 2)
                {
                    var res = CampusPathGraph.ConvertToRouteResponse(path);
                    
                    if (res != null && applyRouteResponse(res))
                    {
                        Debug.Log($"[MenuController#loadRoute]: Campus path loaded — {path.Count} waypoints, {res.routes[0].distance:F0}m");
                        return; // Success — skip Mapbox API
                    }
                    else
                    {
                        Debug.LogWarning("[MenuController#loadRoute]: Campus path conversion failed, falling back to Mapbox API");
                    }
                }
                else
                {
                    Debug.LogWarning("[MenuController#loadRoute]: No campus path found, falling back to Mapbox API");
                }
            }

            // ============================================================
            //  FALLBACK: MAPBOX DIRECTIONS API
            // ============================================================
            var api = new MapboxApi(MapboxToken);
            var loader = new RouteLoader(api, true);
            
            StartCoroutine(
                    loader.LoadRoute(
                        new RouteWaypoint { Type = RouteWaypointType.Location, Location = startLocation },
                        new RouteWaypoint { Type = RouteWaypointType.Location, Location = s.destination },
                        (err, res) =>
                        {
                            if (err != null)
                            {
                                s.ErrorMessage = err;
                                s.Results = new List<GeocodingFeature>();
                                Debug.LogError($"[MenuController#loadRoute]: Route loading failed - {err}");
                                return;
                            }

                            if (res == null || res.routes == null || res.routes.Count == 0)
                            {
                                s.ErrorMessage = "Invalid or empty route response from Mapbox API";
                                s.Results = new List<GeocodingFeature>();
                                Debug.LogError("[MenuController#loadRoute]: RouteResponse is null or has no routes");
                                return;
                            }

                            applyRouteResponse(res);
                        }));
        }

        /// <summary>
        /// Apply a RouteResponse (from either campus pathfinder or Mapbox API) to the UI.
        /// Returns true on success.
        /// </summary>
        private bool applyRouteResponse(RouteResponse res)
        {
            if (MapboxRoute != null && MapboxRoute.BuildRoute(res))
            {
                if (RouteContainer != null) RouteContainer.SetActive(true);

                MapboxRoute.RoutePathRenderer = currentPathRenderer;
                s.View = View.Route;

                currentResponse = res;
                buildMinimapRoute(res);

                float totalDist = res.routes[0].distance;
                // _audioSystem?.SpeakRouteStarted(_destinationName); // Uncomment to enable audio

                Debug.Log("[MenuController#loadRoute]: Route loaded successfully - AR route view activated");
                Debug.Log($"[MenuController#loadRoute]: Route distance: {totalDist / 1000.0f:F2} km");
                return true;
            }
            else
            {
                s.ErrorMessage = "Failed to build route from response";

                s.View = View.SearchMenu;
                Debug.LogError("[MenuController#loadRoute]: BuildRoute failed");
                return false;
            }
        }

        /// <summary>
        /// Validates that a location has valid latitude and longitude values.
        /// </summary>
        private bool IsValidCoordinate(Location location)
        {
            return location.Latitude >= -90 && location.Latitude <= 90 && 
                   location.Longitude >= -180 && location.Longitude <= 180 &&
                   !(location.Latitude == 0 && location.Longitude == 0);
        }

        /// <summary>
        /// Checks if start and destination are too far apart (intercontinental distance).
        /// Uses simple Euclidean distance check: if delta > 50 degrees, likely bad GPS data.
        /// </summary>
        private bool IsTooFarFromDestination(Location start, Location destination)
        {
            if (start == null || destination == null) return true;
            
            double latDelta = System.Math.Abs(start.Latitude - destination.Latitude);
            double lonDelta = System.Math.Abs(start.Longitude - destination.Longitude);
            
            // If coordinates are more than 50 degrees apart, likely an error or bad GPS
            // Pakistan is roughly 30-37N, 61-77E, so anything too far is suspect
            double distance = System.Math.Sqrt(latDelta * latDelta + lonDelta * lonDelta);
            
            if (distance > 50)
            {
                Debug.LogWarning($"[MenuController#IsTooFarFromDestination]: Start and destination too far apart ({distance:F2} degrees). Start: ({start.Latitude}, {start.Longitude}), Destination: ({destination.Latitude}, {destination.Longitude})");
                return true;
            }
            
            return false;
        }

        private GameObject minimapRouteGo;
        private RouteResponse currentResponse;

        private void buildMinimapRoute(RouteResponse res)
        { //    - Constructs a minimap route based on the route response data.
            // Defensive null checks
            if (res == null)
            {
                Debug.LogError("[MenuController#buildMinimapRoute]: RouteResponse is null");
                return;
            }

            if (res.routes == null || res.routes.Count == 0)
            {
                Debug.LogError("[MenuController#buildMinimapRoute]: No routes in response");
                return;
            }

            var route = res.routes[0];
            if (route == null || route.geometry == null || route.geometry.coordinates == null)
            {
                Debug.LogError("[MenuController#buildMinimapRoute]: Route or geometry is null");
                return;
            }

            var geo = route.geometry;
            var vertices = new List<Vector3>();
            var indices = new List<int>();

            var worldPositions = new List<Vector2>();

            foreach (var p in geo.coordinates)
            {
                if (p == null)
                {
                    Debug.LogWarning("[MenuController#buildMinimapRoute]: Skipping null coordinate");
                    continue;
                }

                var pos = Map.GeoToWorldPosition(new Mapbox.Utils.Vector2d(p.Latitude, p.Longitude), true);
                worldPositions.Add(new Vector2(pos.x, pos.z));
            }

            if (worldPositions.Count == 0)
            {
                Debug.LogWarning("[MenuController#buildMinimapRoute]: No valid coordinates for minimap");
                return;
            }

            // ── Guard: material must be assigned ─────────────────────────────
            if (MinimapLineMaterial == null)
            {
                Debug.LogError("[MenuController#buildMinimapRoute]: MinimapLineMaterial is NOT assigned in the Inspector! " +
                               "Assign a material to MenuController.MinimapLineMaterial so the route line is visible.");
                return;
            }

            if (minimapRouteGo != null)
            {
                Object.Destroy(minimapRouteGo);
            }

            minimapRouteGo = new GameObject("minimap route game object");
            minimapRouteGo.layer = MinimapLayer;

            // ── FIX: Raise the route mesh above map tiles (prevents z-fighting) ─
            minimapRouteGo.transform.position = new Vector3(0f, 0.5f, 0f);

            var mesh = minimapRouteGo.AddComponent<MeshFilter>().mesh;

            // ── FIX: Clamp minimum line width so it never becomes invisible ────
            var lineWidth = Mathf.Max(BaseLineWidth * Mathf.Pow(2.0f, Map.Zoom - 18), 0.3f);
            LineBuilder.BuildLineMesh(worldPositions, mesh, lineWidth);

            var meshRenderer = minimapRouteGo.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MinimapLineMaterial;

            // Add directional arrow chevrons along the route
            addMinimapArrows(worldPositions, lineWidth, minimapRouteGo);
            
            Debug.Log($"[MenuController#buildMinimapRoute]: Minimap built with {worldPositions.Count} points");
        }

        /// <summary>
        /// Creates small arrow/chevron triangles along the minimap route at regular intervals,
        /// pointing in the direction of travel so the user can clearly see which way to go.
        /// </summary>
        private void addMinimapArrows(List<Vector2> points, float lineWidth, GameObject parent)
        {
            if (points == null || points.Count < 2) return;

            // Calculate total route length
            float totalLength = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                totalLength += (points[i + 1] - points[i]).magnitude;
            }

            // Place arrows every ~arrowSpacing units along the route, with a minimum count
            float arrowSpacing = Mathf.Max(totalLength / 15f, lineWidth * 8f);
            float arrowSize = lineWidth * 3f; // Arrow size proportional to line width

            float distanceTravelled = arrowSpacing; // Start one spacing in (skip the very start)
            int segmentIndex = 0;
            float segmentProgress = 0f; // how far along current segment (0..segmentLength)

            int arrowCount = 0;
            const int maxArrows = 40; // Safety cap

            while (segmentIndex < points.Count - 1 && arrowCount < maxArrows)
            {
                Vector2 segStart = points[segmentIndex];
                Vector2 segEnd = points[segmentIndex + 1];
                Vector2 segDir = segEnd - segStart;
                float segLength = segDir.magnitude;

                if (segLength < 0.0001f)
                {
                    segmentIndex++;
                    segmentProgress = 0f;
                    continue;
                }

                float remainInSeg = segLength - segmentProgress;
                float distNeeded = arrowSpacing - (distanceTravelled % arrowSpacing);
                if (distNeeded <= 0) distNeeded = arrowSpacing;

                if (distNeeded <= remainInSeg)
                {
                    // Place arrow at this point
                    segmentProgress += distNeeded;
                    distanceTravelled += distNeeded;

                    Vector2 normalizedDir = segDir.normalized;
                    Vector2 pos2D = segStart + normalizedDir * segmentProgress;

                    createArrowChevron(pos2D, normalizedDir, arrowSize, parent);
                    arrowCount++;
                }
                else
                {
                    // Move to next segment
                    distanceTravelled += remainInSeg;
                    segmentIndex++;
                    segmentProgress = 0f;
                }
            }
        }

        /// <summary>
        /// Creates a single arrow chevron (triangle) at the given position pointing in the given direction.
        /// </summary>
        private void createArrowChevron(Vector2 position, Vector2 direction, float size, GameObject parent)
        {
            // Create chevron triangle: tip pointing forward, two back corners
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);

            Vector3 tip = new Vector3(position.x + direction.x * size, 0.01f, position.y + direction.y * size);
            Vector3 backLeft = new Vector3(position.x - perpendicular.x * size * 0.5f, 0.01f, position.y - perpendicular.y * size * 0.5f);
            Vector3 backRight = new Vector3(position.x + perpendicular.x * size * 0.5f, 0.01f, position.y + perpendicular.y * size * 0.5f);

            var arrowGo = new GameObject("minimap_arrow");
            arrowGo.layer = MinimapLayer;
            arrowGo.transform.SetParent(parent.transform, true);

            var mesh = new Mesh();
            mesh.vertices = new Vector3[] { tip, backLeft, backRight };
            mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new int[] { 0, 2, 1 }; // CW winding for up-facing

            arrowGo.AddComponent<MeshFilter>().mesh = mesh;
            var renderer = arrowGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MinimapLineMaterial;
        }

        IEnumerator search()
        {
            var api = new MapboxApi(MapboxToken);
            yield return api.QueryLocal(s.QueryText, true);

            if (api.ErrorMessage != null)
            {
                s.ErrorMessage = api.ErrorMessage;
                s.Results = new List<GeocodingFeature>();
            }
            else
            {
                s.Results = api.QueryLocalResult.features;
                if (s.Results.Count == 0)
                {
                    s.ErrorMessage = "No results found for \"" + s.QueryText + "\"";
                }
            }
        }

        Vector3 lastCameraPos;
        void Update()
        {
        }
    }
}
