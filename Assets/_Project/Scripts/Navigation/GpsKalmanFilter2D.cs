using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject.Navigation
{
    /// <summary>
    /// Lightweight 2D Kalman filter for east/north meters (constant-velocity model).
    /// </summary>
    public sealed class GpsKalmanFilter2D
    {
        readonly float[] _state = new float[4]; // east, north, velE, velN
        readonly float[,] _p = new float[4, 4];
        bool _initialized;
        double _lastTime;

        public float East => _state[0];
        public float North => _state[1];

        public void Reset()
        {
            _initialized = false;
            _lastTime = 0;
            for (int i = 0; i < 4; i++)
            {
                _state[i] = 0f;
                for (int j = 0; j < 4; j++) _p[i, j] = 0f;
            }
        }

        public void Seed(float east, float north)
        {
            _state[0] = east;
            _state[1] = north;
            _state[2] = _state[3] = 0f;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    _p[i, j] = i == j ? (i < 2 ? 25f : 4f) : 0f;
            _initialized = true;
            _lastTime = Time.realtimeSinceStartupAsDouble;
        }

        public void Update(float measEast, float measNorth, float accuracyM)
        {
            double t = Time.realtimeSinceStartupAsDouble;
            if (!_initialized)
            {
                Seed(measEast, measNorth);
                return;
            }

            float dt = Mathf.Clamp((float)(t - _lastTime), 0.02f, 2f);
            _lastTime = t;

            float q = Mathf.Lerp(0.15f, 2.5f, dt);
            Predict(dt, q);

            float r = Mathf.Max(9f, accuracyM * accuracyM * 1.35f);
            float innovE = measEast - _state[0];
            float innovN = measNorth - _state[1];
            float s = _p[0, 0] + _p[1, 1] + 2f * r;
            if (s < 1e-4f) return;

            float k0 = _p[0, 0] / (_p[0, 0] + r);
            float k1 = _p[1, 1] / (_p[1, 1] + r);

            _state[0] += k0 * innovE;
            _state[1] += k1 * innovN;
            _state[2] += 0.25f * innovE / dt;
            _state[3] += 0.25f * innovN / dt;

            _p[0, 0] *= (1f - k0);
            _p[1, 1] *= (1f - k1);
        }

        void Predict(float dt, float processNoise)
        {
            _state[0] += _state[2] * dt;
            _state[1] += _state[3] * dt;
            _p[0, 0] += processNoise + dt * dt * _p[2, 2];
            _p[1, 1] += processNoise + dt * dt * _p[3, 3];
            _p[2, 2] += processNoise;
            _p[3, 3] += processNoise;
        }
    }
}
