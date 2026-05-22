using ARLocation;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject.Navigation
{
    /// <summary>
    /// GPS + compass smoothing and session origin. Single source for navigation pose.
    /// </summary>
    [DisallowMultipleComponent]
    public class NavigationSensorFusion : MonoBehaviour
    {
        public static NavigationSensorFusion Instance { get; private set; }

        [Header("GPS")]
        [Range(0f, 1f)] public float PositionLowPass = 0.08f;
        [Tooltip("Reject fixes worse than this horizontal accuracy (m). 0 = off.")]
        public float MaxHorizontalAccuracyM = 35f;
        public bool UseKalmanFilter = true;

        [Header("Compass")]
        [Range(0f, 0.99f)] public double CompassLowPass = 0.96;
        [Tooltip("Max heading change per second after smoothing (prevents debug overlay / route jumps).")]
        [Range(5f, 90f)] public float MaxHeadingDeltaPerSecond = 18f;
        [Tooltip("Prefer ARLocation provider heading (Android tilt compensation).")]
        public bool UseProviderHeading = true;

        readonly NavigationGeoOrigin _origin = new NavigationGeoOrigin();
        readonly GpsKalmanFilter2D _kalman = new GpsKalmanFilter2D();
        readonly AngleLowPassFilter _headingFilter = new AngleLowPassFilter(0.88);

        Location _lastRaw;
        Location _smoothed;
        float _lastAccuracyM = 20f;
        float _smoothedHeadingDeg = -1f;
        bool _navActive;

        public NavigationGeoOrigin Origin => _origin;
        public Location SmoothedLocation => _smoothed ?? _lastRaw;
        public float SmoothedHeadingDegrees => _smoothedHeadingDeg;
        public float LastHorizontalAccuracyM => _lastAccuracyM;
        public bool HasFix =>
            _origin.IsLocked &&
            (_lastRaw != null || (ARLocationProvider.Instance?.IsEnabled ?? false));

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _headingFilter.SetFactor(CompassLowPass);
        }

        void OnEnable()
        {
            if (ARLocationProvider.Instance != null)
                ARLocationProvider.Instance.OnLocationUpdated.AddListener(OnProviderLocation);
            if (ARLocationProvider.Instance != null)
                ARLocationProvider.Instance.OnCompassUpdated.AddListener(OnProviderCompass);
        }

        void OnDisable()
        {
            if (ARLocationProvider.Instance != null)
                ARLocationProvider.Instance.OnLocationUpdated.RemoveListener(OnProviderLocation);
            if (ARLocationProvider.Instance != null)
                ARLocationProvider.Instance.OnCompassUpdated.RemoveListener(OnProviderCompass);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_navActive) return;
            PollSensors();
        }

        void OnProviderLocation(Location _) { if (_navActive) PollSensors(); }
        void OnProviderCompass(HeadingReading _) { if (_navActive) UpdateHeading(ARLocationProvider.Instance); }

        public void BeginNavigation(Location seedGps)
        {
            _navActive = true;
            _kalman.Reset();
            _smoothed = null;
            _lastRaw = null;
            if (seedGps != null && IsValid(seedGps))
                LockOrigin(seedGps);
            PollSensors();
        }

        public void EndNavigation()
        {
            _navActive = false;
            _origin.Reset();
            _kalman.Reset();
            _smoothed = null;
            _smoothedHeadingDeg = -1f;
        }

        public void LockOrigin(Location gps)
        {
            if (!IsValid(gps)) return;
            _origin.Lock(gps);
            var local = _origin.GeoToLocalEnu(gps);
            _kalman.Seed(local.x, local.z);
        }

        void PollSensors()
        {
            var provider = ARLocationProvider.Instance;
            if (provider == null || !provider.IsEnabled) return;

            var raw = provider.CurrentLocation.ToLocation();
            if (!IsValid(raw)) return;

            _lastAccuracyM = Mathf.Max(1f, (float)provider.CurrentLocation.accuracy);
            _lastRaw = raw;
            if (MaxHorizontalAccuracyM > 0f && _lastAccuracyM > MaxHorizontalAccuracyM)
                return;
            if (!_origin.IsLocked)
                LockOrigin(raw);

            var enu = Location.VectorFromTo(_origin.Origin, raw, true);
            float east = (float)enu.x;
            float north = (float)enu.z;

            if (UseKalmanFilter)
            {
                _kalman.Update(east, north, _lastAccuracyM);
                east = _kalman.East;
                north = _kalman.North;
            }
            else if (_smoothed != null)
            {
                var prev = _origin.GeoToLocalEnu(_smoothed);
                float a = PositionLowPass;
                east = Mathf.Lerp(prev.x, east, a);
                north = Mathf.Lerp(prev.z, north, a);
            }

            _smoothed = Location.LocationFromEnu(
                _origin.Origin,
                east,
                north,
                raw.Altitude);

            UpdateHeading(provider);
        }

        void UpdateHeading(ARLocationProvider provider)
        {
            _headingFilter.SetFactor(CompassLowPass);
            double h;
            if (UseProviderHeading && provider.Provider != null && provider.Provider.IsCompassEnabled)
                h = provider.CurrentHeading.heading;
            else if (Input.compass.enabled)
                h = Input.compass.trueHeading;
            else
                return;

            if (h < 0) h += 360;
            float filtered = (float)_headingFilter.Apply(h);

            if (_smoothedHeadingDeg < 0f)
            {
                _smoothedHeadingDeg = filtered;
                return;
            }

            float maxStep = MaxHeadingDeltaPerSecond * Mathf.Max(Time.deltaTime, 0.016f);
            float delta = Mathf.DeltaAngle(_smoothedHeadingDeg, filtered);
            if (Mathf.Abs(delta) > maxStep)
                filtered = _smoothedHeadingDeg + Mathf.Sign(delta) * maxStep;

            _smoothedHeadingDeg = filtered;
        }

        public static bool IsValid(Location loc) =>
            loc != null && !(loc.Latitude == 0 && loc.Longitude == 0) &&
            loc.Latitude >= -90 && loc.Latitude <= 90 &&
            loc.Longitude >= -180 && loc.Longitude <= 180;
    }
}
