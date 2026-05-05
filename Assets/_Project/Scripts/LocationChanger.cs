using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARLocation;

//This script allows users to change the current location in the AR application.
//It provides a UI interface to input latitude and longitude values and update the mock location provider.
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class LocationChanger : MonoBehaviour
    {
        [Tooltip("Reference to the ARLocationProvider instance")]
        public ARLocationProvider LocationProvider;

        private bool showLocationUI = false;
        private double inputLatitude;
        private double inputLongitude;
        private double inputAltitude = 0;
        private string latitudeInput = "";
        private string longitudeInput = "";
        private string altitudeInput = "0";
        private string statusMessage = "";
        private float statusMessageTimer = 0;

        private GUIStyle _textFieldStyle;
        GUIStyle textFieldStyle()
        {
            if (_textFieldStyle == null)
            {
                _textFieldStyle = new GUIStyle(GUI.skin.textField);
                _textFieldStyle.fontSize = 30;
            }
            return _textFieldStyle;
        }

        private GUIStyle _buttonStyle;
        GUIStyle buttonStyle()
        {
            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button);
                _buttonStyle.fontSize = 30;
            }
            return _buttonStyle;
        }

        private GUIStyle _labelStyle;
        GUIStyle labelStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = 24;
                _labelStyle.wordWrap = true;
            }
            return _labelStyle;
        }

        void Start()
        {
            if (LocationProvider == null)
            {
                LocationProvider = ARLocationProvider.Instance;
            }

            if (LocationProvider != null && LocationProvider.Provider is MockLocationProvider mockProvider)
            {
                // CRITICAL: Ensure we start with PIEAS coordinates
                if ((mockProvider.mockLocation.Latitude == 0 && mockProvider.mockLocation.Longitude == 0) ||
                    (mockProvider.mockLocation.Latitude == -24.499597 && mockProvider.mockLocation.Longitude == -47.868469))
                {
                    // Reset to PIEAS if not properly set
                    mockProvider.mockLocation = new Location
                    {
                        Latitude = 33.65598735240187,
                        Longitude = 73.2649697331715,
                        Altitude = 0
                    };
                    Debug.Log("[LocationChanger#Start]: Mock location reset to PIEAS Campus");
                }
                
                inputLatitude = mockProvider.mockLocation.Latitude;
                inputLongitude = mockProvider.mockLocation.Longitude;
                inputAltitude = mockProvider.mockLocation.Altitude;
                
                latitudeInput = inputLatitude.ToString("F6");
                longitudeInput = inputLongitude.ToString("F6");
                altitudeInput = inputAltitude.ToString("F2");
            }
        }

        void Update()
        {
            // Toggle location UI with L key (or customize as needed)
            if (Input.GetKeyDown(KeyCode.L))
            {
                showLocationUI = !showLocationUI;
            }

            // Update status message timer
            if (statusMessageTimer > 0)
            {
                statusMessageTimer -= Time.deltaTime;
            }
        }

        void OnGUI()
        {
            // LocationChanger UI is currently hidden.
            // To restore, uncomment the block below and remove this comment.
            // The underlying SetLocation() method remains fully available for code use.

            /*
            if (!showLocationUI)
            {
                // Show toggle button
                if (GUI.Button(new Rect(10, 10, 200, 60), "Change Location (L)", buttonStyle()))
                {
                    showLocationUI = true;
                }
                return;
            }

            // Location Change UI
            DrawLocationChangeUI();
            */
        }

        private void DrawLocationChangeUI()
        {
            float panelWidth = 500;
            float panelHeight = 500;
            float x = (Screen.width - panelWidth) / 2;
            float y = (Screen.height - panelHeight) / 2;

            // Background panel
            GUI.Box(new Rect(x, y, panelWidth, panelHeight), "", new GUIStyle(GUI.skin.box));

            GUILayout.BeginArea(new Rect(x + 10, y + 10, panelWidth - 20, panelHeight - 20));

            GUILayout.Label("Change Current Location", new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold });
            GUILayout.Space(10);

            // Latitude input
            GUILayout.Label("Latitude:", labelStyle());
            latitudeInput = GUILayout.TextField(latitudeInput, textFieldStyle(), GUILayout.Height(40));
            GUILayout.Space(10);

            // Longitude input
            GUILayout.Label("Longitude:", labelStyle());
            longitudeInput = GUILayout.TextField(longitudeInput, textFieldStyle(), GUILayout.Height(40));
            GUILayout.Space(10);

            // Altitude input
            GUILayout.Label("Altitude (m):", labelStyle());
            altitudeInput = GUILayout.TextField(altitudeInput, textFieldStyle(), GUILayout.Height(40));
            GUILayout.Space(10);

            // Status message
            if (statusMessageTimer > 0)
            {
                GUI.color = Color.green;
                GUILayout.Label(statusMessage, labelStyle());
                GUI.color = Color.white;
            }

            GUILayout.Space(10);

            // Buttons
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Update Location", buttonStyle(), GUILayout.Height(50)))
            {
                UpdateLocation();
            }

            if (GUILayout.Button("Close", buttonStyle(), GUILayout.Height(50)))
            {
                showLocationUI = false;
            }

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void UpdateLocation()
        {
            // Validate inputs
            if (!double.TryParse(latitudeInput, out inputLatitude))
            {
                statusMessage = "Invalid latitude format";
                statusMessageTimer = 2;
                Debug.LogError($"[LocationChanger]: Invalid latitude: {latitudeInput}");
                return;
            }

            if (!double.TryParse(longitudeInput, out inputLongitude))
            {
                statusMessage = "Invalid longitude format";
                statusMessageTimer = 2;
                Debug.LogError($"[LocationChanger]: Invalid longitude: {longitudeInput}");
                return;
            }

            if (!double.TryParse(altitudeInput, out inputAltitude))
            {
                inputAltitude = 0;
            }

            // Validate ranges
            if (inputLatitude < -90 || inputLatitude > 90)
            {
                statusMessage = "Latitude must be between -90 and 90";
                statusMessageTimer = 2;
                Debug.LogError("[LocationChanger]: Invalid latitude range");
                return;
            }

            if (inputLongitude < -180 || inputLongitude > 180)
            {
                statusMessage = "Longitude must be between -180 and 180";
                statusMessageTimer = 2;
                Debug.LogError("[LocationChanger]: Invalid longitude range");
                return;
            }

            // Update the location
            var newLocation = new Location
            {
                Latitude = inputLatitude,
                Longitude = inputLongitude,
                Altitude = inputAltitude
            };

            // Get the mock provider and update it
            if (LocationProvider != null && LocationProvider.Provider is MockLocationProvider mockProvider)
            {
                mockProvider.mockLocation = newLocation;
                LocationProvider.ForceLocationUpdate();
                
                statusMessage = $"Location updated!\nLat: {inputLatitude:F6}\nLon: {inputLongitude:F6}";
                statusMessageTimer = 3;
                Debug.Log($"[LocationChanger]: Location updated to Lat: {inputLatitude}, Lon: {inputLongitude}, Alt: {inputAltitude}");
            }
            else
            {
                statusMessage = "Error: Mock location provider not available";
                statusMessageTimer = 2;
                Debug.LogError("[LocationChanger]: Provider is not MockLocationProvider");
            }
        }

        /// <summary>
        /// Public method to change location programmatically
        /// </summary>
        public void SetLocation(double latitude, double longitude, double altitude = 0)
        {
            latitudeInput = latitude.ToString("F6");
            longitudeInput = longitude.ToString("F6");
            altitudeInput = altitude.ToString("F2");
            UpdateLocation();
        }

        /// <summary>
        /// Public method to set location from Location object
        /// </summary>
        public void SetLocation(Location location)
        {
            if (location != null)
            {
                SetLocation(location.Latitude, location.Longitude, location.Altitude);
            }
        }
    }
}
