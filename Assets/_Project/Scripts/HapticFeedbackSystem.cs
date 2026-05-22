using UnityEngine;

/// <summary>
/// Provides haptic/vibration feedback for navigation events on Android.
/// Falls back to debug logs on other platforms.
/// </summary>
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class HapticFeedbackSystem : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _vibrator;
#endif

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                Debug.Log("[HapticFeedback] Android Vibrator service acquired.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HapticFeedback] Could not get vibrator: {ex.Message}");
            }
#else
            Debug.Log("[HapticFeedback] Running in Editor — haptics will only be logged.");
#endif
        }

        /// <summary>Short pulse when passing a waypoint (100ms).</summary>
        public void VibrateWaypoint()
        {
            Vibrate(100);
        }

        /// <summary>Double pulse when arriving at destination.</summary>
        public void VibrateArrival()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Pattern: wait 0ms, vibrate 200ms, wait 150ms, vibrate 300ms
            long[] pattern = { 0, 200, 150, 300 };
            try
            {
                if (_vibrator != null)
                    _vibrator.Call("vibrate", pattern, -1); // -1 = don't repeat
                Debug.Log("[HapticFeedback] Arrival vibration pattern");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HapticFeedback] Vibrate error: {ex.Message}");
            }
#else
            Debug.Log("[HapticFeedback:Editor] Arrival vibration (200ms-150ms-300ms pattern)");
#endif
        }

        /// <summary>Quick buzz for UI interactions (50ms).</summary>
        public void VibrateLight()
        {
            Vibrate(50);
        }

        /// <summary>Attention pulse when leaving the route (180ms).</summary>
        public void VibrateWarning()
        {
            Vibrate(180);
        }

        private void Vibrate(long milliseconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_vibrator != null)
                    _vibrator.Call("vibrate", milliseconds);
                Debug.Log($"[HapticFeedback] Vibrate {milliseconds}ms");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HapticFeedback] Vibrate error: {ex.Message}");
            }
#else
            Debug.Log($"[HapticFeedback:Editor] Vibrate {milliseconds}ms");
#endif
        }
    }
}
