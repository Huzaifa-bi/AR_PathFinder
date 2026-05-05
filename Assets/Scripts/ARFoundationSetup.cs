using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

/// <summary>
/// Manages AR Foundation initialization and configuration for Android ARCore.
/// Add this script to an empty GameObject in your startup scene.
/// </summary>
public class ARFoundationSetup : MonoBehaviour
{
    [SerializeField]
    private bool enablePlaneDetection = true;
    
    [SerializeField]
    private bool enableLightEstimation = true;

    [SerializeField]
    private bool enablePointCloud = false;

    private ARSession m_ARSession;
    private ARSessionOrigin m_ARSessionOrigin;
    private ARCameraManager m_CameraManager;
    private ARPlaneManager m_PlaneManager;
    private ARAnchorManager m_AnchorManager;
    private ARRaycastManager m_RaycastManager;

    private void Start()
    {
        StartCoroutine(InitializeARFoundation());
    }

    private IEnumerator InitializeARFoundation()
    {
        // Wait for XR Management to load
        yield return null;

        // Get AR components
        m_ARSession = FindObjectOfType<ARSession>();
        m_ARSessionOrigin = FindObjectOfType<ARSessionOrigin>();
        m_CameraManager = FindObjectOfType<ARCameraManager>();
        m_PlaneManager = FindObjectOfType<ARPlaneManager>();
        m_AnchorManager = FindObjectOfType<ARAnchorManager>();
        m_RaycastManager = FindObjectOfType<ARRaycastManager>();

        // Create components if they don't exist
        if (m_ARSession == null)
        {
            GameObject sessionGO = new GameObject("AR Session");
            m_ARSession = sessionGO.AddComponent<ARSession>();
        }

        if (m_ARSessionOrigin == null)
        {
            GameObject originGO = new GameObject("AR Session Origin");
            m_ARSessionOrigin = originGO.AddComponent<ARSessionOrigin>();
        }

        if (m_CameraManager == null)
        {
            m_CameraManager = m_ARSessionOrigin.gameObject.AddComponent<ARCameraManager>();
        }

        if (m_PlaneManager == null)
        {
            m_PlaneManager = m_ARSessionOrigin.gameObject.AddComponent<ARPlaneManager>();
        }

        if (m_AnchorManager == null)
        {
            m_AnchorManager = m_ARSessionOrigin.gameObject.AddComponent<ARAnchorManager>();
        }

        if (m_RaycastManager == null)
        {
            m_RaycastManager = m_ARSessionOrigin.gameObject.AddComponent<ARRaycastManager>();
        }

        // Configure managers
        ConfigureManagers();

        // Ensure AR Camera has ARCameraBackground
        Camera arCamera = m_ARSessionOrigin.camera;
        if (arCamera != null && arCamera.GetComponent<ARCameraBackground>() == null)
        {
            arCamera.gameObject.AddComponent<ARCameraBackground>();
        }

        Debug.Log("AR Foundation initialized successfully");
    }

    private void ConfigureManagers()
    {
        // Configure plane detection
        if (m_PlaneManager != null)
        {
            if (enablePlaneDetection)
            {
                m_PlaneManager.detectionMode = 
                    PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                m_PlaneManager.enabled = true;
                Debug.Log("Plane detection enabled");
            }
            else
            {
                m_PlaneManager.enabled = false;
            }
        }

        // Point cloud support requires depth subsystem (ARCore feature)
        // Enabled through ARCore settings in Project Settings
    }

    public bool TryRaycast(Vector2 screenPoint, out ARRaycastHit hit)
    {
        hit = default(ARRaycastHit);

        if (m_RaycastManager == null)
            return false;

        var hits = new System.Collections.Generic.List<ARRaycastHit>();
        if (m_RaycastManager.Raycast(screenPoint, hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
        {
            if (hits.Count > 0)
            {
                hit = hits[0];
                return true;
            }
        }

        return false;
    }

    public ARAnchor CreateAnchor(Pose pose)
    {
        if (m_AnchorManager == null)
            return null;

        return m_AnchorManager.AddAnchor(pose);
    }

    public void RemoveAnchor(ARAnchor anchor)
    {
        if (m_AnchorManager == null)
            return;

        m_AnchorManager.RemoveAnchor(anchor);
    }

    public ARSessionOrigin GetSessionOrigin() => m_ARSessionOrigin;
    public ARCameraManager GetCameraManager() => m_CameraManager;
    public ARPlaneManager GetPlaneManager() => m_PlaneManager;
    public ARAnchorManager GetAnchorManager() => m_AnchorManager;
}
