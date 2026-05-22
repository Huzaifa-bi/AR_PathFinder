using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>
    /// AR Foundation session lifecycle: permissions, camera feed, plane visual hygiene.
    /// </summary>
    public class ARSessionBootstrap : MonoBehaviour
    {
        public ARPlaneManager PlaneManager { get; private set; }
        public ARRaycastManager RaycastManager { get; private set; }
        public ARCameraManager CameraManager { get; private set; }

        bool _planeHooksConfigured;

        public void Initialize(ARPlaneManager planeManager, ARRaycastManager raycastManager, ARCameraManager cameraManager)
        {
            PlaneManager = planeManager ?? FindObjectOfType<ARPlaneManager>(true);
            RaycastManager = raycastManager ?? FindObjectOfType<ARRaycastManager>(true);
            CameraManager = cameraManager ?? FindObjectOfType<ARCameraManager>(true);
        }

        public void Begin()
        {
            RequestAndroidPermissions();
            StartCoroutine(ActivateSessionWhenReady());
            StartCoroutine(EnsureMainCameraFeed());
            StartCoroutine(DiagnosticLoop());
        }

        void RequestAndroidPermissions()
        {
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
#endif
        }

        IEnumerator ActivateSessionWhenReady()
        {
#if UNITY_ANDROID
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                yield return new WaitForSeconds(0.5f);
#endif
            var session = FindObjectOfType<ARSession>(true);
            if (session != null)
            {
                session.gameObject.SetActive(true);
                session.enabled = true;
            }

            var origin = FindObjectOfType<ARSessionOrigin>(true);
            if (origin != null)
            {
                origin.gameObject.SetActive(true);
                var cam = origin.camera;
                if (cam != null)
                {
                    if (cam.GetComponent<ARCameraManager>() == null)
                        cam.gameObject.AddComponent<ARCameraManager>();
                    if (cam.GetComponent<ARCameraBackground>() == null)
                        cam.gameObject.AddComponent<ARCameraBackground>();
                    cam.tag = "MainCamera";
                }
            }
        }

        IEnumerator EnsureMainCameraFeed()
        {
            yield return new WaitForSeconds(0.5f);
            Camera arCamera = null;
            var origin = FindObjectOfType<ARSessionOrigin>(true);
            if (origin != null) arCamera = origin.camera;

            foreach (var cam in FindObjectsOfType<Camera>(true))
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

        IEnumerator DiagnosticLoop()
        {
            yield return new WaitForSeconds(1f);
            while (true)
            {
#if UNITY_ANDROID
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                var state = ARSession.state;
                if (state == ARSessionState.None || state == ARSessionState.CheckingAvailability)
                    yield return ARSession.CheckAvailability();
                if (ARSession.state == ARSessionState.NeedsInstall)
                    yield return ARSession.Install();
#endif
                yield return new WaitForSeconds(3f);
            }
        }

        public void ConfigurePlaneVisualization(bool navigationActive)
        {
            if (PlaneManager == null)
                PlaneManager = FindObjectOfType<ARPlaneManager>(true);
            if (PlaneManager == null) return;

            if (navigationActive)
            {
                if (!PlaneManager.enabled)
                    PlaneManager.enabled = true;
                foreach (var plane in PlaneManager.trackables)
                    HidePlaneVisuals(plane);
                return;
            }

            if (!_planeHooksConfigured)
            {
                _planeHooksConfigured = true;
                PlaneManager.planesChanged += OnPlanesChanged;
                StartCoroutine(RescanPlaneVisuals());
            }

            foreach (var plane in PlaneManager.trackables)
                HidePlaneVisuals(plane);
        }

        IEnumerator RescanPlaneVisuals()
        {
            for (int i = 0; i < 12; i++)
            {
                if (PlaneManager != null)
                {
                    foreach (var plane in PlaneManager.trackables)
                        HidePlaneVisuals(plane);
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            if (args.added != null)
                foreach (var p in args.added) HidePlaneVisuals(p);
            if (args.updated != null)
                foreach (var p in args.updated) HidePlaneVisuals(p);
        }

        static void HidePlaneVisuals(ARPlane plane)
        {
            if (plane == null) return;
            foreach (var lr in plane.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr != null) Destroy(lr);
            }
            foreach (var mr in plane.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = false;
            foreach (var mb in plane.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is ARPlane) continue;
                string tn = mb.GetType().Name;
                if (tn.Contains("PlaneMeshVisualizer"))
                    mb.enabled = false;
            }
        }

        void OnDestroy()
        {
            if (PlaneManager != null && _planeHooksConfigured)
                PlaneManager.planesChanged -= OnPlanesChanged;
        }
    }
}
