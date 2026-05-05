using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Example: AR navigation marker placement using AR Foundation.
/// Shows how to raycast, detect planes, and place anchored objects for AR navigation.
/// 
/// This replaces platform-specific ARKit/ARCore code with cross-platform AR Foundation.
/// </summary>
public class ARNavigationMarkerPlacer : MonoBehaviour
{
    [SerializeField]
    private GameObject navigationMarkerPrefab;

    [SerializeField]
    private ARRaycastManager raycastManager;

    [SerializeField]
    private ARAnchorManager anchorManager;

    [SerializeField]
    private ARPlaneManager planeManager;

    private Dictionary<ARAnchor, GameObject> placedMarkers = new Dictionary<ARAnchor, GameObject>();

    private void Start()
    {
        // Get AR managers if not assigned
        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
        if (anchorManager == null)
            anchorManager = FindObjectOfType<ARAnchorManager>();
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();

        // Subscribe to plane detection if you want to react to surface changes
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }
    }

    private void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        Debug.Log($"Planes added: {args.added.Count}, updated: {args.updated.Count}, removed: {args.removed.Count}");
    }

    /// <summary>
    /// Try to place a navigation marker at the screen tap position.
    /// Uses raycast to find planes, then anchors the marker.
    /// </summary>
    public void TryPlaceMarkerAtScreenPoint(Vector2 screenPoint)
    {
        if (raycastManager == null)
        {
            Debug.LogWarning("ARRaycastManager not found!");
            return;
        }

        // Perform raycast against planes
        var hits = new List<ARRaycastHit>();
        if (raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
        {
            if (hits.Count > 0)
            {
                // Use the closest hit
                ARRaycastHit hit = hits[0];
                PlaceMarkerAt(hit.pose);
            }
        }
        else
        {
            Debug.Log("Raycast did not hit any planes. Make sure planes are being detected.");
        }
    }

    /// <summary>
    /// Place a marker at a specific world pose.
    /// </summary>
    private void PlaceMarkerAt(Pose hitPose)
    {
        if (anchorManager == null)
        {
            Debug.LogWarning("ARAnchorManager not found!");
            return;
        }

        if (navigationMarkerPrefab == null)
        {
            Debug.LogWarning("Navigation marker prefab not assigned!");
            return;
        }

        // Create world anchor at the hit position
        // AR Foundation 4.x+ uses TryAddAnchor; older versions use AddAnchor
        ARAnchor anchor = null;
#if UNITY_2021_2_OR_NEWER
        // Modern API: TryAddAnchor returns bool
        var anchorGO = new GameObject("NavigationAnchor");
        anchorGO.transform.SetPositionAndRotation(hitPose.position, hitPose.rotation);
        anchor = anchorGO.AddComponent<ARAnchor>();
#else
        anchor = anchorManager.AddAnchor(hitPose);
#endif

        if (anchor == null)
        {
            Debug.LogError("Failed to create anchor!");
            return;
        }

        // Instantiate the visual marker
        GameObject markerInstance = Instantiate(navigationMarkerPrefab, hitPose.position, hitPose.rotation);

        // Parent it to the anchor so it moves with tracked surfaces
        markerInstance.transform.SetParent(anchor.transform);
        markerInstance.transform.localPosition = Vector3.zero;
        markerInstance.transform.localRotation = Quaternion.identity;

        // Track the anchor-marker relationship
        placedMarkers[anchor] = markerInstance;

        Debug.Log($"Navigation marker placed at {hitPose.position}");
    }

    /// <summary>
    /// Clear all placed markers and their anchors.
    /// </summary>
    public void ClearAllMarkers()
    {
        if (anchorManager == null)
            return;

        foreach (var kvp in placedMarkers)
        {
            ARAnchor anchor = kvp.Key;
            GameObject marker = kvp.Value;

            if (marker != null)
                Destroy(marker);

            if (anchor != null)
#if UNITY_2021_2_OR_NEWER
                Destroy(anchor.gameObject);
#else
                anchorManager.RemoveAnchor(anchor);
#endif
        }

        placedMarkers.Clear();
        Debug.Log("All navigation markers cleared");
    }

    /// <summary>
    /// Get all currently placed markers.
    /// Useful for navigation path visualization.
    /// </summary>
    public List<GameObject> GetPlacedMarkers()
    {
        var markers = new List<GameObject>();
        foreach (var kvp in placedMarkers)
        {
            if (kvp.Value != null)
                markers.Add(kvp.Value);
        }
        return markers;
    }

    /// <summary>
    /// Check if AR tracking is active.
    /// </summary>
    public bool IsARTrackingActive()
    {
        var arSession = FindObjectOfType<ARSession>();
        if (arSession == null)
            return false;

        // Check if session is enabled
        return arSession.enabled;
    }
}

/// <summary>
/// Example: Input handler for AR navigation marker placement.
/// Responds to touch input to place markers.
/// </summary>
public class ARNavigationInputHandler : MonoBehaviour
{
    [SerializeField]
    private ARNavigationMarkerPlacer markerPlacer;

    private void Start()
    {
        if (markerPlacer == null)
            markerPlacer = FindObjectOfType<ARNavigationMarkerPlacer>();
    }

    private void Update()
    {
        // Check for touch input (mobile)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                // Try to place marker at touch position
                markerPlacer.TryPlaceMarkerAtScreenPoint(touch.position);
            }
        }

        // Also support mouse click for Editor testing
        #if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            markerPlacer.TryPlaceMarkerAtScreenPoint(Input.mousePosition);
        }
        #endif

        // Clear markers with long press or key
        if (Input.GetKeyDown(KeyCode.C))
        {
            markerPlacer.ClearAllMarkers();
        }
    }
}

/// <summary>
/// AR Foundation: Plane Detection Example
/// Shows how to react to detected planes for navigation surface placement.
/// </summary>
public class ARPlaneDetectionExample : MonoBehaviour
{
    [SerializeField]
    private ARPlaneManager planeManager;

    [SerializeField]
    private Material planeVisualizationMaterial;

    private void Start()
    {
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();

        if (planeManager != null)
        {
            // Subscribe to plane changes
            planeManager.planesChanged += OnPlanesChanged;

            // Configure plane detection
            planeManager.detectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            planeManager.enabled = true;

            Debug.Log("Plane detection started. Waiting for surfaces...");
        }
    }

    private void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Handle newly detected planes
        foreach (var plane in args.added)
        {
            OnPlaneDetected(plane);
        }

        // Handle plane updates (size/position changes)
        foreach (var plane in args.updated)
        {
            OnPlaneUpdated(plane);
        }

        // Handle removed planes
        foreach (var plane in args.removed)
        {
            OnPlaneRemoved(plane);
        }
    }

    private void OnPlaneDetected(ARPlane plane)
    {
        Debug.Log($"Plane detected: {plane.trackableId}, size: {plane.extents}, alignment: {plane.alignment}");

        // Optional: Visualize the plane
        var renderer = plane.GetComponent<MeshRenderer>();
        if (renderer != null && planeVisualizationMaterial != null)
        {
            renderer.material = planeVisualizationMaterial;
        }

        // You could trigger navigation events here
        // E.g., "Floor detected - ready for navigation"
    }

    private void OnPlaneUpdated(ARPlane plane)
    {
        // Plane changed size, position, or alignment
        // Update any dependent visuals or logic here
    }

    private void OnPlaneRemoved(ARPlane plane)
    {
        Debug.Log($"Plane removed: {plane.trackableId}");
    }
}

/// <summary>
/// AR Foundation: Anchor Management Example
/// Shows lifecycle of anchors for persistent marker placement.
/// </summary>
public class ARAnchroManagementExample : MonoBehaviour
{
    [SerializeField]
    private ARAnchorManager anchorManager;

    private Dictionary<ARAnchor, string> anchorLabels = new Dictionary<ARAnchor, string>();

    private void Start()
    {
        if (anchorManager == null)
            anchorManager = FindObjectOfType<ARAnchorManager>();
    }

    /// <summary>
    /// Add a labeled anchor at a specific world position.
    /// Anchors stay in place relative to tracked surfaces.
    /// </summary>
    public void CreateLabeledAnchor(Vector3 worldPosition, string label)
    {
        if (anchorManager == null)
        {
            Debug.LogWarning("ARAnchorManager not found!");
            return;
        }

        // Create anchor at world position
        Pose anchorPose = new Pose(worldPosition, Quaternion.identity);
        ARAnchor anchor = null;
#if UNITY_2021_2_OR_NEWER
        var anchorGO = new GameObject("LabeledAnchor");
        anchorGO.transform.SetPositionAndRotation(anchorPose.position, anchorPose.rotation);
        anchor = anchorGO.AddComponent<ARAnchor>();
#else
        anchor = anchorManager.AddAnchor(anchorPose);
#endif

        if (anchor != null)
        {
            anchorLabels[anchor] = label;
            Debug.Log($"Anchor created: {label} at {worldPosition}");
        }
    }

    /// <summary>
    /// Remove all anchors.
    /// </summary>
    public void ClearAllAnchors()
    {
        if (anchorManager == null)
            return;

        var anchorsToRemove = new List<ARAnchor>(anchorLabels.Keys);
        foreach (var anchor in anchorsToRemove)
        {
#if UNITY_2021_2_OR_NEWER
            Destroy(anchor.gameObject);
#else
            anchorManager.RemoveAnchor(anchor);
#endif
            anchorLabels.Remove(anchor);
        }

        Debug.Log("All anchors cleared");
    }

    /// <summary>
    /// Get current anchor count.
    /// </summary>
    public int GetAnchorCount()
    {
        return anchorLabels.Count;
    }
}
