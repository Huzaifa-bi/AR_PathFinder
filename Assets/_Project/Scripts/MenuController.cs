using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARLocation;
using ARLocation.MapboxRoutes;
using ARLocation.MapboxRoutes.SampleProject.Navigation;
using Mapbox.Unity.Map;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>
    /// Campus AR navigation orchestrator: routing, minimap, HUD, and ground chevrons.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        public string MapboxToken = PIEASConfig.MapboxToken;
        public ARCameraManager ARCameraManager;
        public ARPlaneManager ARPlaneManager;
        public ARRaycastManager ARRaycastManager;
        public GameObject RouteContainer;
        public Camera Camera;
        public Camera MapboxMapCamera;
        public MapboxRoute MapboxRoute;
        public AbstractRouteRenderer RoutePathRenderer;
        public AbstractRouteRenderer NextTargetPathRenderer;
        public Texture RenderTexture;
        public AbstractMap Map;
        public int MinimapLayer;
        public Material MinimapLineMaterial;
        public float BaseLineWidth = 3f;

        const float RecalcHoldSeconds = 2.5f;
        const float ArrivalThresholdM = 12f;
        const float NavUpdateInterval = 0.5f;
        const float MapCenterUpdateInterval = 3f;

        enum NavView { Search, Preview, Navigating }

        class NavState
        {
            public NavView View = NavView.Search;
            public Location Destination;
            public string QueryText = "";
            public List<GeocodingFeature> SearchResults = new List<GeocodingFeature>();
        }

        readonly NavState _state = new NavState();

        ARSessionBootstrap _arBootstrap;
        NavigationMinimapService _minimap;
        NavigationAudioSystem _audio;
        ARNavigationUI _ui;
        HapticFeedbackSystem _haptics;
        ARGuidanceSystem _guidance;
        NavigationSensorFusion _sensorFusion;
        NavigationDebugOverlay _debugOverlay;
        NullRoutePathRenderer _nullRenderer;

        RouteResponse _activeRoute;
        string _destinationName = "Destination";
        int _routeSessionId;
        bool _arActive;
        bool _applyLiveOnNextRoute;
        Coroutine _routeCoroutine;
        Coroutine _previewCoroutine;

        int _stepIndex;
        bool _approachSpoken;
        bool _turnSpoken;
        float _navTimer;
        float _offRouteTimer;
        float _smoothDist = -1f;
        float _rerouteCooldownUntil;
        float _minimapOrthoBaseline = -1f;
        RouteDeviationLevel _lastDeviationLevel = RouteDeviationLevel.None;

        void Awake()
        {
            MapboxToken = PIEASConfig.MapboxToken;
            _arBootstrap = GetComponent<ARSessionBootstrap>() ?? gameObject.AddComponent<ARSessionBootstrap>();
            _arBootstrap.Initialize(ARPlaneManager, ARRaycastManager, ARCameraManager);
            _arBootstrap.Begin();
        }

        void Start()
        {
            if (ARLocationProvider.Instance?.Provider is MockLocationProvider mock &&
                (mock.mockLocation.Latitude == 0 && mock.mockLocation.Longitude == 0))
            {
                mock.mockLocation = new Location
                {
                    Latitude = PIEASConfig.CenterLatitude,
                    Longitude = PIEASConfig.CenterLongitude
                };
            }

            if (Map == null)
                Map = FindObjectOfType<AbstractMap>();
            if (Map != null)
            {
                Map.SetCenterLatitudeLongitude(PIEASConfig.CampusCenter);
                Map.SetZoom(PIEASConfig.DefaultZoomLevel);
                ApplySatelliteBasemap();
                StartCoroutine(CoUpdateMapNextFrame());
            }

            if (MapboxMapCamera != null && MapboxMapCamera.orthographic)
                _minimapOrthoBaseline = MapboxMapCamera.orthographicSize;

            _minimap = new NavigationMinimapService(Map, MapboxMapCamera, MinimapLayer, MinimapLineMaterial, BaseLineWidth);

            _audio = GetComponent<NavigationAudioSystem>() ?? gameObject.AddComponent<NavigationAudioSystem>();
            _haptics = GetComponent<HapticFeedbackSystem>() ?? gameObject.AddComponent<HapticFeedbackSystem>();
            _guidance = GetComponent<ARGuidanceSystem>();
            if (_guidance == null)
                _guidance = gameObject.AddComponent<ARGuidanceSystem>();
            _guidance.Bind(ARPlaneManager, ARRaycastManager);
            _sensorFusion = GetComponent<NavigationSensorFusion>();
            if (_sensorFusion == null)
                _sensorFusion = gameObject.AddComponent<NavigationSensorFusion>();
            _debugOverlay = GetComponent<NavigationDebugOverlay>();
            if (_debugOverlay == null)
                _debugOverlay = gameObject.AddComponent<NavigationDebugOverlay>();

            _ui = GetComponent<ARNavigationUI>() ?? gameObject.AddComponent<ARNavigationUI>();
            _ui.Initialize(RenderTexture);
            if (RenderTexture is RenderTexture rt && rt.width > 0 && rt.height > 0 && MapboxMapCamera != null)
                MapboxMapCamera.aspect = (float)rt.width / rt.height;

            ConfigureLocationProvider();
            WireUiEvents();
            ConfigureMapboxRouteForAR();
            RefreshLocationList();

            if (ARLocationProvider.Instance != null)
                ARLocationProvider.Instance.OnEnabled.AddListener(OnGpsEnabled);
            if (Map != null)
                Map.OnUpdated += OnMapRedrawn;
        }

        void WireUiEvents()
        {
            _ui.OnSearchRequested += OnSearchRequested;
            _ui.OnLocationSelected += OnLocationSelected;
            _ui.OnSearchResultSelected += OnSearchResultSelected;
            _ui.OnEndNavigation += EndNavigation;
            _ui.OnStartARNavigation += StartArNavigation;
            _ui.OnCancelRoutePreview += CancelPreview;
            _ui.OnRerouteRequested += OnRerouteRequested;
        }

        void OnDisable()
        {
            if (ARLocationProvider.Instance != null)
                ARLocationProvider.Instance.OnEnabled.RemoveListener(OnGpsEnabled);
            if (Map != null)
                Map.OnUpdated -= OnMapRedrawn;
        }

        void OnDestroy()
        {
            _minimap?.Dispose();
        }

        void Update()
        {
            if (_ui == null) return;

            _ui.UpdateARStatus(ARSession.state.ToString());
            _arBootstrap.ConfigurePlaneVisualization(_arActive);

            if (_state.View == NavView.Search)
            {
                if (Time.frameCount % 300 == 0)
                    RefreshLocationList();
                return;
            }

            if (_state.View != NavView.Navigating || !_arActive || _activeRoute?.routes == null || _activeRoute.routes.Count == 0)
                return;

            var user = GetNavigationLocation();
            if (user == null) return;

            if (_arActive && _guidance != null && _guidance.IsActive)
                _guidance.UpdateGuidance(user);

            _navTimer += Time.deltaTime;
            if (_navTimer < NavUpdateInterval) return;
            _navTimer = 0f;

            TickNavigation(user);
        }

        // ── UI handlers ──────────────────────────────────────────────────────

        void OnSearchRequested(string query)
        {
            _state.QueryText = query ?? "";
            var campus = GetCampusLocations();
            string q = _state.QueryText.Trim().ToLowerInvariant();
            for (int i = 0; i < campus.Count; i++)
            {
                if (campus[i].Name.ToLowerInvariant().Contains(q))
                {
                    BeginRouteTo(campus[i].Name, new Location
                    {
                        Latitude = campus[i].Coordinates.x,
                        Longitude = campus[i].Coordinates.y
                    });
                    return;
                }
            }
            StartCoroutine(CoSearchMapbox());
        }

        void OnLocationSelected(int index)
        {
            var campus = GetCampusLocations();
            if (index < 0 || index >= campus.Count) return;
            var loc = campus[index];
            BeginRouteTo(loc.Name, new Location { Latitude = loc.Coordinates.x, Longitude = loc.Coordinates.y });
        }

        void OnSearchResultSelected(int index)
        {
            if (_state.SearchResults == null || index < 0 || index >= _state.SearchResults.Count) return;
            var r = _state.SearchResults[index];
            var coords = r.geometry?.coordinates;
            if (coords == null || coords.Count == 0)
            {
                _ui.ShowError("Search result has no coordinates.");
                return;
            }
            BeginRouteTo(r.place_name, coords[0]);
        }

        void OnRerouteRequested()
        {
            if (!_arActive || _state.Destination == null) return;
            if (Time.time < _rerouteCooldownUntil)
            {
                _ui.SetGuidanceFooter("Please wait before re-routing again.");
                return;
            }
            var user = GetUserLocation();
            if (user == null)
            {
                _ui.ShowError("GPS not ready — cannot re-route.");
                return;
            }
            _rerouteCooldownUntil = Time.time + 12f;
            _ui.SetGuidanceFooter("Updating path…");
            _ui.SetRerouteButtonInteractable(false);
            _applyLiveOnNextRoute = true;
            LoadRoute(user);
        }

        public void StartRoute(Location dest) => BeginRouteTo(_destinationName, dest);

        void BeginRouteTo(string name, Location dest)
        {
            _routeSessionId++;
            StopRouteCoroutines();
            TeardownRoute();

            _destinationName = name;
            _state.Destination = dest;
            _stepIndex = 0;
            _approachSpoken = _turnSpoken = false;
            _navTimer = 0f;
            _smoothDist = -1f;

            var start = (ARLocationProvider.Instance?.IsEnabled ?? false)
                ? ARLocationProvider.Instance.CurrentLocation.ToLocation()
                : new Location { Latitude = PIEASConfig.CenterLatitude, Longitude = PIEASConfig.CenterLongitude };
            LoadRoute(start);
        }

        void StartArNavigation()
        {
            if (_activeRoute == null) return;
            ConfigureMapboxRouteForAR();
            var seed = GetUserLocation();
            _sensorFusion?.BeginNavigation(seed);
            FreezeArOrientationForNavigation(true);
            MapboxRoute?.SetTarget(0);
            StartGuidance(_activeRoute);
            _arActive = true;
            if (RouteContainer != null)
            {
                RouteContainer.SetActive(true);
                DisableLegacyRouteUi();
            }
            _state.View = NavView.Navigating;
            _stepIndex = 0;
            _offRouteTimer = 0f;
            _smoothDist = -1f;
            _lastDeviationLevel = RouteDeviationLevel.None;
            _ui.ClearRouteDeviation();

            if (MapboxMapCamera != null && MapboxMapCamera.orthographic && _minimapOrthoBaseline > 0f)
                MapboxMapCamera.orthographicSize = _minimapOrthoBaseline;

            _ui.ShowNavScreen();
            _ui.SetDestinationName(_destinationName);
            _ui.UpdateDistanceRemaining(_activeRoute.routes[0].distance);
            _ui.ClearGuidanceFooter();
            _ui.SetRerouteButtonInteractable(true);
            _audio?.SpeakRouteStarted(_destinationName);
        }

        void CancelPreview()
        {
            _routeSessionId++;
            StopRouteCoroutines();
            _arActive = false;
            _applyLiveOnNextRoute = false;
            TeardownRoute();
            _state.View = NavView.Search;
            _state.Destination = null;
            _ui.ShowSearchScreen();
            RefreshLocationList();
        }

        public void EndRoute() => EndNavigation();

        public void EndNavigation()
        {
            _routeSessionId++;
            StopRouteCoroutines();
            _arActive = false;
            _applyLiveOnNextRoute = false;
            FreezeArOrientationForNavigation(false);
            _sensorFusion?.EndNavigation();
            TeardownRoute();
            _state.View = NavView.Search;
            _state.Destination = null;
            _ui.ShowSearchScreen();
            _ui.ClearGuidanceFooter();
            _ui.ClearRouteDeviation();
            _lastDeviationLevel = RouteDeviationLevel.None;
            RefreshLocationList();
        }

        public void CancelRouteDueToArrival()
        {
            EndNavigation();
            _ui.ShowSuccess($"You have arrived at {_destinationName}!");
            _audio?.SpeakArrived(_destinationName);
            _haptics?.VibrateArrival();
        }

        // ── Routing ──────────────────────────────────────────────────────────

        void LoadRoute(Location start)
        {
            if (_state.Destination == null) return;
            if (!IsValidCoord(start))
                start = new Location { Latitude = PIEASConfig.CenterLatitude, Longitude = PIEASConfig.CenterLongitude };
            if (!IsValidCoord(_state.Destination))
            {
                _ui.ShowError("Invalid destination coordinates.");
                return;
            }

            int session = _routeSessionId;
            var startV = new Mapbox.Utils.Vector2d(start.Latitude, start.Longitude);
            var destV = new Mapbox.Utils.Vector2d(_state.Destination.Latitude, _state.Destination.Longitude);
            bool onCampus = PIEASConfig.IsWithinCampusBounds(startV) && PIEASConfig.IsWithinCampusBounds(destV);
            bool roadRouting = PIEASConfig.ActiveTravelProfile == PIEASConfig.RouteTravelProfile.Driving;

            if (roadRouting)
                _routeCoroutine = StartCoroutine(CoMapboxOnly(start, _state.Destination, session));
            else if (onCampus && PIEASConfig.MapboxWalkingFirstOnCampus)
                _routeCoroutine = StartCoroutine(CoMapboxThenCampus(start, _state.Destination, session));
            else if (onCampus)
                TryCampusGraphOrMapbox(start, _state.Destination, session);
            else
                _routeCoroutine = StartCoroutine(CoMapboxOnly(start, _state.Destination, session));
        }

        void TryCampusGraphOrMapbox(Location start, Location dest, int session)
        {
            var path = CampusPathGraph.Instance.FindPath(start.Latitude, start.Longitude, dest.Latitude, dest.Longitude);
            if (path != null && path.Count >= 2)
            {
                var res = CampusPathGraph.ConvertToRouteResponse(path);
                if (res != null && ApplyRoute(res, session)) return;
            }
            _routeCoroutine = StartCoroutine(CoMapboxOnly(start, dest, session));
        }

        IEnumerator CoMapboxOnly(Location start, Location dest, int session)
        {
            yield return CampusDirectionsBroker.LoadWalkingRoute(
                MapboxToken,
                Waypoint(start), Waypoint(dest),
                true, true,
                (err, res) => OnRouteLoaded(err, res, session));
            _routeCoroutine = null;
        }

        IEnumerator CoMapboxThenCampus(Location start, Location dest, int session)
        {
            string err = null;
            RouteResponse res = null;
            yield return CampusDirectionsBroker.LoadWalkingRoute(
                MapboxToken, Waypoint(start), Waypoint(dest), true, true,
                (e, r) => { err = e; res = r; });
            _routeCoroutine = null;
            if (session != _routeSessionId) yield break;

            if (string.IsNullOrEmpty(err) && res?.routes?.Count > 0)
            {
                res.KeepShortestRouteOnly();
                ApplyRoute(res, session);
                yield break;
            }

            if (PIEASConfig.ActiveTravelProfile != PIEASConfig.RouteTravelProfile.Driving)
            {
                var path = CampusPathGraph.Instance.FindPath(start.Latitude, start.Longitude, dest.Latitude, dest.Longitude);
                if (path != null && path.Count >= 2)
                {
                    var campusRes = CampusPathGraph.ConvertToRouteResponse(path);
                    if (campusRes != null && ApplyRoute(campusRes, session))
                        yield break;
                }
            }

            _ui.ShowError(string.IsNullOrEmpty(err) ? "Could not compute route." : err);
        }

        void OnRouteLoaded(string err, RouteResponse res, int session)
        {
            if (session != _routeSessionId) return;
            if (!string.IsNullOrEmpty(err) || res?.routes == null || res.routes.Count == 0)
            {
                _ui.ShowError(err ?? "Empty route response.");
                if (_arActive) _ui.SetRerouteButtonInteractable(true);
                return;
            }
            res.KeepShortestRouteOnly();
            ApplyRoute(res, session);
        }

        bool ApplyRoute(RouteResponse res, int session)
        {
            if (session != _routeSessionId) return false;

            bool live = _applyLiveOnNextRoute;
            _applyLiveOnNextRoute = false;

            ConfigureMapboxRouteForAR();
            if (MapboxRoute == null || !MapboxRoute.BuildRoute(res))
            {
                _ui.ShowError("Failed to build route.");
                return false;
            }

            _activeRoute = res;
            float totalDist = res.routes[0].distance;

            if (live)
            {
                _arActive = true;
                _state.View = NavView.Navigating;
                if (RouteContainer != null)
                {
                    RouteContainer.SetActive(true);
                    DisableLegacyRouteUi();
                }
                _ui.ShowNavScreen();
                _ui.SetDestinationName(_destinationName);
                _ui.UpdateDistanceRemaining(totalDist);
                _ui.SetRerouteButtonInteractable(true);
                StartCoroutine(CoRefreshLiveVisuals(res));
                return true;
            }

            _arActive = false;
            if (RouteContainer != null) RouteContainer.SetActive(false);
            _guidance?.EndSession();
            if (_previewCoroutine != null) StopCoroutine(_previewCoroutine);
            _previewCoroutine = StartCoroutine(CoShowPreview(res, totalDist, session));
            return true;
        }

        IEnumerator CoShowPreview(RouteResponse res, float dist, int session)
        {
            yield return null;
            if (session != _routeSessionId) yield break;
            _minimap.FrameRoute(res);
            yield return CoUpdateMapNextFrame();
            _minimap.BuildRoute(res);
            _minimap.FitCameraToRoute(res);
            if (session != _routeSessionId) yield break;
            _state.View = NavView.Preview;
            _ui.ShowRoutePreview(_destinationName, dist);
            _previewCoroutine = null;
        }

        IEnumerator CoRefreshLiveVisuals(RouteResponse res)
        {
            yield return CoUpdateMapNextFrame();
            _minimap.BuildRoute(res);
            StartGuidance(res);
        }

        IEnumerator CoSearchMapbox()
        {
            var api = new MapboxApi(MapboxToken);
            yield return api.QueryLocal(_state.QueryText, true);
            if (api.ErrorMessage != null)
            {
                _ui.ShowError(api.ErrorMessage);
                _state.SearchResults.Clear();
            }
            else
            {
                _state.SearchResults = api.QueryLocalResult.features;
                if (_state.SearchResults.Count == 0)
                    _ui.ShowError($"No results for \"{_state.QueryText}\"");
                else
                {
                    var names = new List<string>();
                    foreach (var r in _state.SearchResults) names.Add(r.place_name);
                    _ui.ShowSearchResults(names);
                }
            }
        }

        // ── Live navigation tick ───────────────────────────────────────────────

        void TickNavigation(Location user)
        {
            var route = _activeRoute.routes[0];
            var steps = route.legs?[0].steps;
            var geom = route.geometry?.coordinates;
            if (steps == null || steps.Count == 0 || geom == null || geom.Count == 0) return;

            var dest = geom[geom.Count - 1];
            float rawDist = (float)CampusPathGraph.HaversineDistance(
                user.Latitude, user.Longitude, dest.Latitude, dest.Longitude);

            if (_smoothDist < 0) _smoothDist = rawDist;
            else if (Mathf.Abs(rawDist - _smoothDist) < 500f)
                _smoothDist = Mathf.Lerp(_smoothDist, rawDist, Time.deltaTime * 1.5f);

            _ui.UpdateDistanceRemaining(_smoothDist);
            _minimap.FollowUser(user, MapCenterUpdateInterval);

            if (_smoothDist < ArrivalThresholdM)
            {
                CancelRouteDueToArrival();
                return;
            }

            if (_stepIndex < steps.Count - 1 && steps[_stepIndex + 1].maneuver != null)
            {
                var m = steps[_stepIndex + 1].maneuver;
                float dNext = (float)CampusPathGraph.HaversineDistance(
                    user.Latitude, user.Longitude, m.location.Latitude, m.location.Longitude);
                if (dNext < 8f)
                {
                    _stepIndex++;
                    MapboxRoute?.SetTarget(Mathf.Min(_stepIndex, steps.Count - 1));
                    _approachSpoken = _turnSpoken = false;
                    _haptics?.VibrateWaypoint();
                }
            }

            float heading = _sensorFusion != null && _sensorFusion.SmoothedHeadingDegrees >= 0f
                ? _sensorFusion.SmoothedHeadingDegrees
                : (Input.compass.enabled ? Input.compass.trueHeading : -1f);
            bool lastStep = _stepIndex >= steps.Count - 1;
            float distManeuver = -1f;
            string arrow = "↑";
            string instruction;

            if (lastStep)
            {
                instruction = $"Head to {_destinationName}";
                arrow = "◉";
                _guidance.ClearNextTurn();
            }
            else if (steps[_stepIndex + 1].maneuver != null)
            {
                var next = steps[_stepIndex + 1];
                distManeuver = (float)CampusPathGraph.HaversineDistance(
                    user.Latitude, user.Longitude,
                    next.maneuver.location.Latitude, next.maneuver.location.Longitude);

                if (distManeuver < 50f && !_approachSpoken)
                {
                    _audio?.SpeakApproachingTurn(next.maneuver.instruction ?? "", distManeuver);
                    _approachSpoken = true;
                }
                if (distManeuver < 8f && !_turnSpoken)
                {
                    _audio?.SpeakAtTurn(next.maneuver.instruction ?? "");
                    _turnSpoken = true;
                }

                instruction = NavigationInstructionFormatter.BuildBannerLine(
                    steps[_stepIndex], next, next.maneuver, distManeuver, _destinationName, heading);
                arrow = NavigationInstructionFormatter.ArrowForManeuver(next.maneuver, heading);
                _guidance.SetNextTurn(next.maneuver.location);
            }
            else
            {
                instruction = NavigationInstructionFormatter.BuildContinueToward(_destinationName);
                _guidance.ClearNextTurn();
            }

            _ui.UpdateInstruction(arrow, instruction, distManeuver);
            if (_smoothDist < 30f)
                _audio?.SpeakApproachingDestination();

            float offRoute = NavigationGeometry.MinDistanceMetersToPolyline(user.Latitude, user.Longitude, geom);
            float softM = PIEASConfig.CampusOffRouteWarningM;
            float hardM = PIEASConfig.CampusOffRouteRecalcM;

            if (offRoute <= softM)
            {
                _offRouteTimer = 0f;
                if (_lastDeviationLevel != RouteDeviationLevel.None)
                {
                    _lastDeviationLevel = RouteDeviationLevel.None;
                    _ui.ClearRouteDeviation();
                }
            }
            else if (offRoute <= hardM)
            {
                _offRouteTimer = 0f;
                if (_lastDeviationLevel != RouteDeviationLevel.Warning)
                {
                    _lastDeviationLevel = RouteDeviationLevel.Warning;
                    _audio?.SpeakOffRoute(offRoute);
                    _haptics?.VibrateWarning();
                }
                _ui.SetRouteDeviation(RouteDeviationLevel.Warning, offRoute, false);
            }
            else
            {
                _offRouteTimer += Time.deltaTime;
                bool recalc = _offRouteTimer >= RecalcHoldSeconds;
                if (_lastDeviationLevel != RouteDeviationLevel.Recalculating)
                {
                    _lastDeviationLevel = RouteDeviationLevel.Recalculating;
                    _audio?.SpeakOffRoute(offRoute);
                    _haptics?.VibrateWarning();
                }
                _ui.SetRouteDeviation(RouteDeviationLevel.Recalculating, offRoute, recalc);
                if (recalc)
                {
                    _audio?.SpeakRecalculating();
                    _haptics?.VibrateWaypoint();
                    _offRouteTimer = 0f;
                    _smoothDist = -1f;
                    _applyLiveOnNextRoute = true;
                    LoadRoute(user);
                }
            }
        }

        // ── AR guidance / Mapbox route ───────────────────────────────────────

        static void ConfigureLocationProvider()
        {
            var provider = ARLocationProvider.Instance;
            if (provider == null) return;
            provider.LocationProviderSettings.TimeBetweenUpdates = 1f;
            provider.LocationProviderSettings.MinDistanceBetweenUpdates = 0.5;
            provider.LocationProviderSettings.AccuracyRadius = 40;
            provider.LocationProviderSettings.MaxNumberOfUpdates = 0;
        }

        void ConfigureMapboxRouteForAR()
        {
            ARGuidanceSystem.SanitizeScene();
            DisableLegacyRouteUi();
            _nullRenderer = GetComponent<NullRoutePathRenderer>() ?? gameObject.AddComponent<NullRoutePathRenderer>();
            if (RoutePathRenderer != null) RoutePathRenderer.enabled = false;
            if (NextTargetPathRenderer != null) NextTargetPathRenderer.enabled = false;
            if (MapboxRoute == null) return;
            MapboxRoute.Settings.EnableSignpostBehaviour = false;
            MapboxRoute.Settings.LoadRouteAtStartup = false;
            MapboxRoute.RoutePathRenderer = _nullRenderer;
            MapboxRoute.SuppressLegacyArObjects(enableSignposts: false);
        }

        /// <summary>Hide legacy Mapbox sample UI (Line to Target, etc.); HUD is ARNavigationUI + chevrons.</summary>
        static void DisableLegacyRouteUi()
        {
            var route = GameObject.Find("Route");
            if (route == null) return;

            foreach (var canvas in route.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = false;

            string[] legacyNames =
            {
                "Canvas", "ArMenuCanvas", "ButtonLineRender", "ButtonExit",
                "TargetLineRender", "TargetExit", "TargetNext", "pinpoint",
            };

            foreach (Transform t in route.GetComponentsInChildren<Transform>(true))
            {
                for (int i = 0; i < legacyNames.Length; i++)
                {
                    if (t.name != legacyNames[i]) continue;
                    t.gameObject.SetActive(false);
                    break;
                }
            }
        }

        static void FreezeArOrientationForNavigation(bool freeze)
        {
            var orient = ARLocationOrientation.Instance;
            if (orient != null)
                orient.SetNavigationFrozen(freeze);
        }

        void StartGuidance(RouteResponse res)
        {
            var coords = res.routes[0].geometry?.coordinates;
            if (coords == null || coords.Count < 2) return;
            if (ARPlaneManager == null) ARPlaneManager = FindObjectOfType<ARPlaneManager>(true);
            if (ARRaycastManager == null) ARRaycastManager = FindObjectOfType<ARRaycastManager>(true);
            _guidance.Bind(ARPlaneManager, ARRaycastManager);
            ConfigureMapboxRouteForAR();
            ARGuidanceSystem.SanitizeScene();
            _guidance.BeginSession(coords);
            _guidance.UpdateGuidance(GetUserLocation());
        }

        void TeardownRoute()
        {
            _guidance?.EndSession();
            MapboxRoute?.HideAllSignposts();
            MapboxRoute?.ClearBuiltRoute();
            _minimap?.ClearRoute();
            if (RouteContainer != null) RouteContainer.SetActive(false);
            _activeRoute = null;
        }

        void StopRouteCoroutines()
        {
            if (_routeCoroutine != null) { StopCoroutine(_routeCoroutine); _routeCoroutine = null; }
            if (_previewCoroutine != null) { StopCoroutine(_previewCoroutine); _previewCoroutine = null; }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static RouteWaypoint Waypoint(Location loc) =>
            new RouteWaypoint { Type = RouteWaypointType.Location, Location = loc };

        Location GetUserLocation()
        {
            if (ARLocationProvider.Instance?.IsEnabled == true)
            {
                var loc = ARLocationProvider.Instance.CurrentLocation.ToLocation();
                if (IsValidCoord(loc)) return loc;
            }
            return new Location { Latitude = PIEASConfig.CenterLatitude, Longitude = PIEASConfig.CenterLongitude };
        }

        Location GetNavigationLocation()
        {
            if (_arActive && _sensorFusion != null && _sensorFusion.HasFix)
            {
                var sm = _sensorFusion.SmoothedLocation;
                if (IsValidCoord(sm)) return sm;
            }
            return GetUserLocation();
        }

        static bool IsValidCoord(Location loc) =>
            loc != null && !(loc.Latitude == 0 && loc.Longitude == 0) &&
            loc.Latitude >= -90 && loc.Latitude <= 90 &&
            loc.Longitude >= -180 && loc.Longitude <= 180;

        void OnGpsEnabled(Location loc)
        {
            if (Map == null || loc == null) return;
            Map.SetCenterLatitudeLongitude(new Mapbox.Utils.Vector2d(loc.Latitude, loc.Longitude));
            StartCoroutine(CoUpdateMapNextFrame());
        }

        void OnMapRedrawn()
        {
            if (_activeRoute != null)
                _minimap.BuildRoute(_activeRoute);
        }

        IEnumerator CoUpdateMapNextFrame()
        {
            yield return null;
            Map?.UpdateMap();
        }

        void ApplySatelliteBasemap()
        {
            if (Map == null || !PIEASConfig.UseSatelliteStreetBasemap) return;
            try
            {
                Map.ImageLayer?.SetLayerSource(ImagerySourceType.MapboxSatelliteStreet);
                Map.ImageLayer?.UseRetina(true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MenuController] Basemap: {e.Message}");
            }
        }

        void RefreshLocationList()
        {
            if (_ui == null) return;
            double lat = PIEASConfig.CenterLatitude, lon = PIEASConfig.CenterLongitude;
            bool hasGps = false;
            if (ARLocationProvider.Instance?.IsEnabled ?? false)
            {
                var u = ARLocationProvider.Instance.CurrentLocation.ToLocation();
                if (IsValidCoord(u)) { lat = u.Latitude; lon = u.Longitude; hasGps = true; }
            }

            var items = new List<(string, string, float)>();
            foreach (var loc in GetCampusLocations())
            {
                float d = hasGps
                    ? (float)CampusPathGraph.HaversineDistance(lat, lon, loc.Coordinates.x, loc.Coordinates.y)
                    : -1f;
                items.Add((loc.Name, loc.Description, d));
            }
            _ui.SetLocationsListWithDistance(items);
        }

        List<CampusLocation> GetCampusLocations() =>
            CampusLocations.Instance != null
                ? CampusLocations.Instance.GetAllLocations()
                : new List<CampusLocation>();
    }
}
