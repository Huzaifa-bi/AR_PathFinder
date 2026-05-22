using UnityEngine;

/// <summary>
/// Google Maps-style turn-by-turn navigation audio using Android TextToSpeech.
///
/// STATUS: Component is attached and TTS is initialised on Android, but no
/// Speak*() methods are called from MenuController yet.
/// To activate audio, uncomment the _audioSystem calls in MenuController.cs
/// (look for "// _audioSystem?" comment lines).
/// </summary>
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class NavigationAudioSystem : MonoBehaviour
    {
        [Header("TTS Settings")]
        [Tooltip("Speech rate — 1.0 = normal, 0.88 = slightly slower / clearer")]
        [Range(0.5f, 2f)]
        public float SpeechRate = 0.88f;

        [Tooltip("Minimum seconds between successive audio cues (prevents overlap)")]
        [Range(1f, 10f)]
        public float CooldownSeconds = 3.5f;

        private float _lastSpeakTime = -999f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _tts;
        private bool              _ttsReady = false;
#endif

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InitTTS();
#else
            Debug.Log("[NavigationAudio] Running in Editor — TTS cues will only be logged.");
#endif
        }

        void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ShutdownTTS();
#endif
        }

        // ── Public Cue API (mirrors Google Maps audio events) ────────────────

        /// <summary>Call when a new route is loaded. e.g. "Starting navigation to Library"</summary>
        public void SpeakRouteStarted(string destination)
            => Speak($"Starting navigation to {destination}");

        /// <summary>
        /// Call ~300 m before a turn.
        /// e.g. "In 300 meters, turn right toward C-Block"
        /// </summary>
        public void SpeakApproachingTurn(string turnInstruction, float distanceMeters)
        {
            string prefix = distanceMeters >= 400 ? "In 300 meters, " :
                            distanceMeters >= 150 ? "In 100 meters, " :
                            distanceMeters >= 60  ? "In 50 meters, "  : "";
            Speak($"{prefix}{turnInstruction.ToLower()}");
        }

        /// <summary>Call when the user reaches/passes a waypoint turn.</summary>
        public void SpeakAtTurn(string turnInstruction) => Speak(turnInstruction);

        /// <summary>Call when the destination is ~30 m away.</summary>
        public void SpeakApproachingDestination() => Speak("Your destination is ahead");

        /// <summary>Call on arrival.</summary>
        public void SpeakArrived(string destination)
            => Speak($"You have arrived at {destination}");

        /// <summary>Call when the route needs to be recalculated.</summary>
        public void SpeakRecalculating() => Speak("Recalculating");

        /// <summary>Call when the user leaves the path.</summary>
        public void SpeakOffRoute(float metersOff)
        {
            if (metersOff >= 20f)
                Speak("You are off route. Recalculating.");
            else
                Speak("You left the path. Return to the white arrows.");
        }

        // ── Core speak (with cooldown to prevent overlap) ────────────────────

        public void Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.realtimeSinceStartup;
            if (now - _lastSpeakTime < CooldownSeconds) return;
            _lastSpeakTime = now;

#if UNITY_ANDROID && !UNITY_EDITOR
            SpeakOnAndroid(text);
#else
            Debug.Log($"[NavigationAudio:TTS] \"{text}\"");
#endif
        }

        // ── Android TextToSpeech Implementation ──────────────────────────────

#if UNITY_ANDROID && !UNITY_EDITOR

        void InitTTS()
        {
            try
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");

                _tts = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    activity,
                    new TTSInitListener(OnTTSReady));

                Debug.Log("[NavigationAudio] Android TTS object created.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NavigationAudio] TTS initialisation error: {ex.Message}");
            }
        }

        void ShutdownTTS()
        {
            if (_tts == null) return;
            try { _tts.Call("stop"); _tts.Call("shutdown"); } catch { /* ignore */ }
            _tts.Dispose();
            _tts = null;
        }

        void OnTTSReady(bool success)
        {
            _ttsReady = success;
            if (!success)
            {
                Debug.LogWarning("[NavigationAudio] Android TTS initialisation failed.");
                return;
            }
            try
            {
                _tts.Call<int>("setSpeechRate", SpeechRate);
                // Force English (US) so locale setting on the device doesn't change words
                var locale = new AndroidJavaObject("java.util.Locale", "en", "US");
                _tts.Call<int>("setLanguage", locale);
                Debug.Log("[NavigationAudio] Android TTS ready (en-US).");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NavigationAudio] TTS setup error: {ex.Message}");
            }
        }

        void SpeakOnAndroid(string text)
        {
            if (_tts == null || !_ttsReady) return;
            try
            {
                // QUEUE_FLUSH (0) = interrupt any current speech immediately
                _tts.Call<int>("speak", text, 0, null, "ar_nav_cue");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NavigationAudio] TTS speak error: {ex.Message}");
            }
        }

        /// <summary>
        /// JNI proxy that implements android.speech.tts.TextToSpeech.OnInitListener.
        /// Unity routes the Java callback back to managed code via this class.
        /// </summary>
        class TTSInitListener : AndroidJavaProxy
        {
            readonly System.Action<bool> _callback;

            public TTSInitListener(System.Action<bool> callback)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _callback = callback;
            }

            // Called by Android — status 0 = SUCCESS
            public void onInit(int status) => _callback?.Invoke(status == 0);
        }

#endif  // UNITY_ANDROID && !UNITY_EDITOR
    }
}
