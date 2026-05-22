using System.Collections;
using System.Collections.Generic;
using ARLocation;
using ARLocation.MapboxRoutes.SampleProject.Navigation;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>
    /// AR ground chevrons on the route centerline. World positions are rebuilt each frame from
    /// Kalman-smoothed GPS snapped to the route so the path stays on the road without one-shot bake drift.
    /// </summary>
    public class ARGuidanceSystem : MonoBehaviour
    {
        [Header("Chevrons")]
        [Range(1.5f, 12f)] public float ChevronSpacing = 2.5f;
        [Range(0.4f, 3f)] public float ChevronSize = 1.4f;
        [Range(8, 120)] public int MaxChevrons = 48;
        [Range(5f, 80f)] public float MaxChevronDistanceM = 45f;

        [Header("Turn markers")]
        [Range(10f, 80f)] public float TurnMarkerShowWithinM = 42f;
        [Range(0.5f, 3f)] public float TurnMarkerScale = 1.6f;

        static readonly Color ChevronWhite = new Color(1f, 1f, 1f, 0.98f);
        static readonly Color TurnBlue = new Color(0.18f, 0.42f, 0.92f, 0.95f);

        readonly List<Location> _polyline = new List<Location>();
        readonly List<Vector3> _frozenPath = new List<Vector3>();
        readonly List<Vector3> _worldPath = new List<Vector3>();
        readonly List<Vector3> _chevronDisplayPos = new List<Vector3>();
        readonly List<ChevronSlot> _chevronPool = new List<ChevronSlot>();
        readonly List<ChevronTarget> _chevronTargets = new List<ChevronTarget>();

        struct ChevronTarget
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        sealed class ChevronSlot
        {
            public GameObject Go;
        }

        Transform _visualRoot;
        GameObject _turnMarkerRoot;
        Vector3 _turnMarkerPos;
        Material _chevronMat;
        Material _turnMat;
        bool _active;
        bool _pathBaked;
        bool _groundYLocked;
        Location _nextTurnLoc;
        bool _hasNextTurn;
        float _groundY;
        NavigationSensorFusion _fusion;
        int _routeSegmentIndex = -1;
        float _smoothedArcM;
        float _arcSmoothVel;
        bool _arcInitialized;

        void Awake() => ApplyDemoStableDefaults();

        /// <summary>Overrides scene Inspector so demo builds stay consistent.</summary>
        public void ApplyDemoStableDefaults()
        {
            ChevronSpacing = 2.5f;
            ChevronSize = 0.9f;
            MaxChevrons = 32;
            MaxChevronDistanceM = 38f;
            TurnMarkerScale = 1.35f;
        }

        public bool IsActive => _active;
        public int LastChevronCount { get; private set; }
        public int LastWorldPathPointCount { get; private set; }
        public string LastStatus { get; private set; } = "idle";

        public void Bind(ARPlaneManager planeManager, ARRaycastManager raycastManager) { }

        public void BeginSession(IList<Location> routeCoordinates)
        {
            StopAllCoroutines();
            StartCoroutine(CoBeginSession(routeCoordinates));
        }

        IEnumerator CoBeginSession(IList<Location> routeCoordinates)
        {
            EndSession();
            _polyline.Clear();
            if (routeCoordinates != null)
            {
                var simplified = NavigationGeometry.SimplifyPolylineForDisplay(
                    routeCoordinates, PIEASConfig.CampusPathSimplifySpacingM);
                for (int i = 0; i < simplified.Count; i++)
                    _polyline.Add(simplified[i]);
            }

            _fusion = NavigationSensorFusion.Instance ?? FindObjectOfType<NavigationSensorFusion>();
            ApplyDemoStableDefaults();
            StrengthenFusionForDemo();
            EnsureRouteVisualRoot();
            EnsureMaterials();
            SanitizeScene();

            float wait = 0f;
            while (wait < 3f && !TryResolveUserLocation(out _))
            {
                wait += Time.deltaTime;
                yield return null;
            }

            float settle = 0f;
            while (settle < 3f)
            {
                settle += Time.deltaTime;
                yield return null;
            }

            _routeSegmentIndex = -1;
            _pathBaked = false;
            _groundYLocked = false;
            _frozenPath.Clear();
            _chevronDisplayPos.Clear();
            _arcInitialized = false;
            _smoothedArcM = 0f;
            _arcSmoothVel = 0f;
            _active = true;
            LastStatus = _polyline.Count < 2 ? "no polyline" : "active";
            yield return null;
            TickVisuals();
        }

        public void EndSession()
        {
            _active = false;
            _polyline.Clear();
            _worldPath.Clear();
            _chevronTargets.Clear();
            _hasNextTurn = false;
            _pathBaked = false;
            _groundYLocked = false;
            _frozenPath.Clear();
            _chevronDisplayPos.Clear();
            _routeSegmentIndex = -1;
            _smoothedArcM = 0f;
            _arcSmoothVel = 0f;
            _arcInitialized = false;
            LastChevronCount = 0;
            LastWorldPathPointCount = 0;
            LastStatus = "ended";
            ClearChevronPool();
            ClearTurnMarker();
        }

        public void SetNextTurn(Location maneuverLocation)
        {
            _hasNextTurn = maneuverLocation != null;
            _nextTurnLoc = maneuverLocation;
        }

        public void ClearNextTurn()
        {
            _hasNextTurn = false;
            ClearTurnMarker();
        }

        public void UpdateGuidance(Location userGps) { }

        void Update()
        {
            if (!_active) return;
            TickVisuals();
        }

        void TickVisuals()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                LastStatus = "no camera";
                return;
            }

            if (_polyline.Count < 2)
            {
                LastStatus = "polyline<2";
                return;
            }

            if (!TryResolveUserLocation(out Location user))
            {
                LastStatus = "no gps";
                return;
            }

            var arRoot = ResolveArRoot();
            if (arRoot == null)
            {
                LastStatus = "no ar root";
                return;
            }

            Location onRoute = SnapUserToRoute(user, out float progressHeading);
            LockGroundY(cam.transform.position);

            if (!_pathBaked)
                BakePathOnce(arRoot, cam.transform, onRoute);
            else
                RefreshWorldPathYOnly();

            if (_worldPath.Count < 2)
            {
                LastStatus = "path<2";
                LastChevronCount = 0;
                return;
            }

            LastWorldPathPointCount = _worldPath.Count;
            float arcStart = UpdateSmoothedArc(onRoute, progressHeading);
            UpdateChevronTargets(_worldPath, arcStart);
            ApplyChevronPositions();

            if (_hasNextTurn)
                UpdateTurnMarker(arRoot, cam.transform, onRoute);

            LastStatus = $"ok chevrons={LastChevronCount} seg={_routeSegmentIndex}";
        }

        /// <summary>Snap smoothed GPS to route centerline; heading only used near corners.</summary>
        Location SnapUserToRoute(Location user, out float headingDeg)
        {
            headingDeg = -1f;
            if (user == null || _polyline.Count < 2)
                return user;

            float turnAhead = _routeSegmentIndex >= 0
                ? NavigationGeometry.GetTurnAngleAfterSegment(_polyline, _routeSegmentIndex)
                : 0f;

            if (turnAhead >= PIEASConfig.CornerProgressHeadingMinDeg)
            {
                float compass = _fusion != null ? _fusion.SmoothedHeadingDegrees : -1f;
                if (compass >= 0f)
                    headingDeg = compass;
            }

            int seg = _routeSegmentIndex;
            float snapM = PIEASConfig.PathBakeSnapMaxM;
            if (snapM <= 0f)
                return user;

            var snapped = NavigationGeometry.SnapToRoute(user, _polyline, snapM, ref seg, headingDeg);
            _routeSegmentIndex = seg;
            return snapped;
        }

        void StrengthenFusionForDemo()
        {
            if (_fusion == null) return;
            _fusion.PositionLowPass = 0.05f;
            _fusion.CompassLowPass = 0.97;
            _fusion.MaxHeadingDeltaPerSecond = 12f;
            _fusion.UseKalmanFilter = true;
        }

        void BakePathOnce(Transform arRoot, Transform cam, Location onRoute)
        {
            _frozenPath.Clear();
            if (onRoute == null) return;

            _groundY = cam.position.y - PIEASConfig.PhoneHeightAboveGroundM;
            _groundYLocked = true;
            float y = GetChevronGroundY();

            for (int i = 0; i < _polyline.Count; i++)
            {
                var w = Location.GetGameObjectPositionForLocation(
                    arRoot, cam, onRoute, _polyline[i], true);
                w.y = y;
                _frozenPath.Add(w);
            }

            _pathBaked = _frozenPath.Count >= 2;
            RefreshWorldPathYOnly();
        }

        void RefreshWorldPathYOnly()
        {
            _worldPath.Clear();
            float y = GetChevronGroundY();
            for (int i = 0; i < _frozenPath.Count; i++)
            {
                var w = _frozenPath[i];
                w.y = y;
                _worldPath.Add(w);
            }
        }

        float UpdateSmoothedArc(Location onRoute, float headingDeg)
        {
            if (onRoute == null)
                return _smoothedArcM;

            int seg = _routeSegmentIndex;
            if (!NavigationGeometry.TryGetArcLengthOnPolyline(
                    onRoute.Latitude, onRoute.Longitude, _polyline, headingDeg,
                    ref seg, out float rawArc, out _))
            {
                return _smoothedArcM;
            }

            _routeSegmentIndex = seg;

            if (!_arcInitialized)
            {
                _smoothedArcM = rawArc;
                _arcInitialized = true;
                return _smoothedArcM;
            }

            float smooth = Mathf.SmoothDamp(_smoothedArcM, rawArc, ref _arcSmoothVel, 1.05f);
            if (smooth < _smoothedArcM - 0.25f)
                smooth = _smoothedArcM;
            float maxStep = Mathf.Max(0.8f, 1.8f * Time.deltaTime);
            if (smooth > _smoothedArcM + maxStep)
                smooth = _smoothedArcM + maxStep;

            _smoothedArcM = smooth;
            return _smoothedArcM;
        }

        bool TryResolveUserLocation(out Location user)
        {
            user = null;
            if (_fusion != null)
            {
                user = _fusion.SmoothedLocation;
                if (NavigationSensorFusion.IsValid(user))
                    return true;
            }

            var provider = ARLocationProvider.Instance;
            if (provider != null && provider.IsEnabled)
            {
                user = provider.CurrentLocation.ToLocation();
                if (NavigationSensorFusion.IsValid(user))
                    return true;
            }

            return false;
        }

        static Transform ResolveArRoot()
        {
            if (ARLocationManager.Instance != null)
                return ARLocationManager.Instance.transform;

            var origin = Object.FindObjectOfType<ARSessionOrigin>(true);
            return origin != null ? origin.transform : null;
        }

        void EnsureRouteVisualRoot()
        {
            var arRoot = ResolveArRoot();
            if (arRoot == null) return;

            if (_visualRoot != null) return;
            var existing = arRoot.Find("ARGuidanceVisuals");
            if (existing != null)
            {
                _visualRoot = existing;
                _visualRoot.localScale = Vector3.one;
                return;
            }

            var go = new GameObject("ARGuidanceVisuals");
            go.transform.SetParent(arRoot, false);
            go.transform.localScale = Vector3.one;
            _visualRoot = go.transform;
        }

        void LockGroundY(Vector3 cameraPosition)
        {
            if (_groundYLocked) return;
            float target = cameraPosition.y - PIEASConfig.PhoneHeightAboveGroundM;
            float t = 1f - Mathf.Exp(-Time.deltaTime / 3f);
            _groundY = Mathf.Lerp(_groundY, target, t * 0.06f);
        }

        float GetChevronGroundY() =>
            _groundY + PIEASConfig.CampusPathGroundOffsetM;

        void UpdateChevronTargets(List<Vector3> path, float arcStartM)
        {
            _chevronTargets.Clear();
            if (path.Count < 2) return;

            float spacing = Mathf.Max(0.4f, ChevronSpacing);
            int cap = Mathf.Clamp(MaxChevrons, 4, 120);
            float groundY = GetChevronGroundY();
            float totalLen = PathLength(path);

            float walk = arcStartM + spacing * 0.25f;
            int n = 0;

            while (walk < totalLen && n < cap)
            {
                if (!SamplePath(path, walk, out Vector3 pos, out Vector3 fwd))
                    break;

                if (walk - arcStartM > MaxChevronDistanceM)
                    break;

                pos.y = groundY;
                _chevronTargets.Add(new ChevronTarget
                {
                    Position = pos,
                    Rotation = FacingAlongPath(fwd)
                });
                n++;
                walk += spacing;
            }

            LastChevronCount = n;
            EnsureChevronPool(n);
        }

        static float PathLength(List<Vector3> path)
        {
            float len = 0f;
            for (int i = 0; i < path.Count - 1; i++)
                len += Vector3.Distance(path[i], path[i + 1]);
            return len;
        }

        void EnsureChevronPool(int count)
        {
            while (_chevronPool.Count < count)
            {
                var go = CreateChevronGo(Vector3.zero, Quaternion.identity);
                go.SetActive(false);
                _chevronPool.Add(new ChevronSlot { Go = go });
            }

            for (int i = 0; i < _chevronPool.Count; i++)
            {
                if (_chevronPool[i].Go != null)
                    _chevronPool[i].Go.SetActive(i < count);
            }
        }

        void ApplyChevronPositions()
        {
            int count = Mathf.Min(_chevronTargets.Count, _chevronPool.Count);
            while (_chevronDisplayPos.Count < count)
                _chevronDisplayPos.Add(_chevronTargets[_chevronDisplayPos.Count].Position);

            float blend = 1f - Mathf.Exp(-Time.deltaTime / 0.28f);
            for (int i = 0; i < count; i++)
            {
                var slot = _chevronPool[i];
                if (slot.Go == null) continue;

                var target = _chevronTargets[i];
                _chevronDisplayPos[i] = Vector3.Lerp(_chevronDisplayPos[i], target.Position, blend);
                slot.Go.transform.SetPositionAndRotation(_chevronDisplayPos[i], target.Rotation);
                slot.Go.transform.localScale = Vector3.one * ChevronSize;
            }
        }

        void ClearChevronPool()
        {
            for (int i = 0; i < _chevronPool.Count; i++)
            {
                if (_chevronPool[i].Go != null)
                    Destroy(_chevronPool[i].Go);
            }
            _chevronPool.Clear();
            _chevronTargets.Clear();
            LastChevronCount = 0;
        }

        static bool SamplePath(List<Vector3> path, float arc, out Vector3 pos, out Vector3 fwd)
        {
            pos = path[0];
            fwd = path.Count > 1 ? path[1] - path[0] : Vector3.forward;
            float acc = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float sl = Vector3.Distance(path[i], path[i + 1]);
                if (acc + sl >= arc)
                {
                    float segT = sl > 1e-6f ? (arc - acc) / sl : 0f;
                    pos = Vector3.Lerp(path[i], path[i + 1], segT);
                    fwd = path[i + 1] - path[i];
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    return true;
                }
                acc += sl;
            }

            pos = path[path.Count - 1];
            fwd = path[path.Count - 1] - path[path.Count - 2];
            fwd.y = 0f;
            return true;
        }

        static Quaternion FacingAlongPath(Vector3 pathForward)
        {
            pathForward.y = 0f;
            if (pathForward.sqrMagnitude < 1e-6f) pathForward = Vector3.forward;
            return Quaternion.LookRotation(pathForward.normalized, Vector3.up);
        }

        GameObject CreateChevronGo(Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("AR_Chevron");
            if (_visualRoot != null)
                go.transform.SetParent(_visualRoot, true);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * ChevronSize;

            var mesh = new Mesh();
            float w = 0.7f, d = 1f, t = 0.18f;
            mesh.vertices = new[]
            {
                new Vector3(0, 0, d),
                new Vector3(-w, 0, 0),
                new Vector3(-w + t, 0, t * 0.5f),
                new Vector3(0, 0, d - t),
                new Vector3(0, 0, d),
                new Vector3(0, 0, d - t),
                new Vector3(w - t, 0, t * 0.5f),
                new Vector3(w, 0, 0),
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 7, 4, 5, 6,
                0, 2, 1, 0, 3, 2,
                4, 7, 6, 4, 6, 5,
            };
            var norms = new Vector3[8];
            for (int i = 0; i < 8; i++) norms[i] = Vector3.up;
            mesh.normals = norms;
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _chevronMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        void UpdateTurnMarker(Transform arRoot, Transform cam, Location onRoute)
        {
            if (onRoute == null || _nextTurnLoc == null) return;

            double dist = CampusPathGraph.HaversineDistance(
                onRoute.Latitude, onRoute.Longitude,
                _nextTurnLoc.Latitude, _nextTurnLoc.Longitude);

            if (dist > TurnMarkerShowWithinM)
            {
                ClearTurnMarker();
                return;
            }

            var pos = Location.GetGameObjectPositionForLocation(
                arRoot, cam, onRoute, _nextTurnLoc, true);
            pos.y = GetChevronGroundY() + 0.08f;

            if (_turnMarkerRoot == null)
            {
                _turnMarkerRoot = CreateTurnArrowMesh();
                _turnMarkerPos = pos;
            }

            float blend = 1f - Mathf.Exp(-Time.deltaTime / 0.35f);
            _turnMarkerPos = Vector3.Lerp(_turnMarkerPos, pos, blend);
            _turnMarkerRoot.SetActive(true);
            _turnMarkerRoot.transform.position = _turnMarkerPos;

            var viewCam = Camera.main;
            if (viewCam != null)
            {
                Vector3 flatFwd = viewCam.transform.forward;
                flatFwd.y = 0f;
                if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.forward;
                _turnMarkerRoot.transform.rotation = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
            }

            _turnMarkerRoot.transform.localScale = Vector3.one * TurnMarkerScale;
        }

        GameObject CreateTurnArrowMesh()
        {
            var go = new GameObject("AR_TurnMarker");
            if (_visualRoot != null)
                go.transform.SetParent(_visualRoot, true);
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(0, 0, 0.9f),
                new Vector3(-0.35f, 0, 0),
                new Vector3(0.35f, 0, 0),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _turnMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void ClearTurnMarker()
        {
            if (_turnMarkerRoot != null)
            {
                Destroy(_turnMarkerRoot);
                _turnMarkerRoot = null;
            }
        }

        public static void SanitizeScene()
        {
            DestroyIfExists("[RoutePathRenderer]");
            DestroyIfExists("[NextStepRoutePathRenderer]");
            DestroyIfExists("ARGroundPath");

            var lines = Object.FindObjectsOfType<LineRenderer>(true);
            for (int i = 0; i < lines.Length; i++)
            {
                var lr = lines[i];
                if (lr == null) continue;
                string n = lr.gameObject.name;
                if (lr.GetComponentInParent<ARPlane>() != null ||
                    n.Contains("RoutePath") || n.Contains("NextStep") || n.Contains("GroundPath"))
                    Object.Destroy(lr.gameObject);
            }
        }

        static void DestroyIfExists(string name)
        {
            var stale = GameObject.Find(name);
            if (stale != null) Object.Destroy(stale);
        }

        void EnsureMaterials()
        {
            if (_chevronMat == null) _chevronMat = CreateVisibleMaterial(ChevronWhite);
            if (_turnMat == null) _turnMat = CreateVisibleMaterial(TurnBlue);
        }

        static Material CreateVisibleMaterial(Color c)
        {
            string[] shaderNames =
            {
                "Unlit/Color",
                "Mobile/Unlit (Supports Lightmap)",
                "Sprites/Default",
                "Legacy Shaders/Unlit/Transparent",
                "Standard",
            };

            foreach (var shaderName in shaderNames)
            {
                var sh = Shader.Find(shaderName);
                if (sh == null) continue;
                var m = new Material(sh);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                else m.color = c;
                if (m.HasProperty("_Cull")) m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m.renderQueue = 3100;
                return m;
            }

            return new Material(Shader.Find("Standard")) { color = c };
        }

        void OnDestroy()
        {
            EndSession();
            if (_chevronMat != null) Destroy(_chevronMat);
            if (_turnMat != null) Destroy(_turnMat);
        }
    }
}
