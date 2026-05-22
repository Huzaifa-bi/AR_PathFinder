using System.Collections.Generic;
using ARLocation;
using ARLocation.MapboxRoutes;
using Mapbox.Unity.Map;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>Builds the orthographic minimap route mesh and player marker.</summary>
    public class NavigationMinimapService
    {
        readonly AbstractMap _map;
        readonly Camera _mapCamera;
        readonly int _layer;
        readonly Material _lineMaterial;
        readonly float _baseLineWidth;

        GameObject _routeMesh;
        GameObject _playerMarker;
        Material _playerDotMaterial;
        float _mapUpdateTimer;

        public NavigationMinimapService(AbstractMap map, Camera mapCamera, int layer, Material lineMaterial, float baseLineWidth)
        {
            _map = map;
            _mapCamera = mapCamera;
            _layer = layer;
            _lineMaterial = lineMaterial;
            _baseLineWidth = baseLineWidth;
        }

        public void ClearRoute()
        {
            if (_routeMesh != null)
            {
                Object.Destroy(_routeMesh);
                _routeMesh = null;
            }
        }

        public void BuildRoute(RouteResponse res)
        {
            ClearRoute();
            if (_map == null || _lineMaterial == null || res?.routes == null || res.routes.Count == 0)
                return;

            var coords = res.routes[0].geometry?.coordinates;
            if (coords == null || coords.Count == 0) return;

            var worldPositions = new List<Vector2>();
            foreach (var p in coords)
            {
                if (p == null) continue;
                var pos = _map.GeoToWorldPosition(new Mapbox.Utils.Vector2d(p.Latitude, p.Longitude), false);
                worldPositions.Add(new Vector2(pos.x, pos.z));
            }
            if (worldPositions.Count == 0) return;

            _routeMesh = new GameObject("MinimapRoute");
            _routeMesh.layer = _layer;
            _routeMesh.transform.position = new Vector3(0f, 0.5f, 0f);

            var mesh = _routeMesh.AddComponent<MeshFilter>().mesh;
            float lineWidth = Mathf.Max(_baseLineWidth * Mathf.Pow(2f, _map.Zoom - 18f), 0.3f);
            LineBuilder.BuildLineMesh(worldPositions, mesh, lineWidth);

            var renderer = _routeMesh.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _lineMaterial;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        }

        public void FrameRoute(RouteResponse res)
        {
            if (_map == null || res?.routes == null || res.routes.Count == 0) return;
            var coords = res.routes[0].geometry?.coordinates;
            if (coords == null || coords.Count == 0) return;

            double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;
            foreach (var p in coords)
            {
                if (p == null) continue;
                if (p.Latitude < minLat) minLat = p.Latitude;
                if (p.Latitude > maxLat) maxLat = p.Latitude;
                if (p.Longitude < minLon) minLon = p.Longitude;
                if (p.Longitude > maxLon) maxLon = p.Longitude;
            }

            _map.SetCenterLatitudeLongitude(new Mapbox.Utils.Vector2d((minLat + maxLat) * 0.5, (minLon + maxLon) * 0.5));
            double span = System.Math.Max(System.Math.Abs(maxLat - minLat), System.Math.Abs(maxLon - minLon));
            float zoom = span > 0.012 ? 14.75f : span > 0.006 ? 15.5f : span > 0.003 ? 16.5f : 17.25f;
            _map.SetZoom(zoom);
        }

        public void FitCameraToRoute(RouteResponse res)
        {
            if (_mapCamera == null || !_mapCamera.orthographic || _map == null) return;
            var coords = res.routes[0].geometry?.coordinates;
            if (coords == null || coords.Count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in coords)
            {
                if (p == null) continue;
                var w = _map.GeoToWorldPosition(new Mapbox.Utils.Vector2d(p.Latitude, p.Longitude), false);
                minX = Mathf.Min(minX, w.x);
                maxX = Mathf.Max(maxX, w.x);
                minZ = Mathf.Min(minZ, w.z);
                maxZ = Mathf.Max(maxZ, w.z);
            }

            float half = Mathf.Clamp(Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f * 1.18f, 8f, 650f);
            var cp = _mapCamera.transform.position;
            cp.x = (minX + maxX) * 0.5f;
            cp.z = (minZ + maxZ) * 0.5f;
            _mapCamera.transform.position = cp;
            _mapCamera.orthographicSize = half;
        }

        public void FollowUser(Location userLoc, float centerUpdateInterval)
        {
            if (_map == null || _mapCamera == null || userLoc == null) return;

            var world = _map.GeoToWorldPosition(new Mapbox.Utils.Vector2d(userLoc.Latitude, userLoc.Longitude), false);
            var cp = _mapCamera.transform.position;
            cp.x = world.x;
            cp.z = world.z;
            _mapCamera.transform.position = cp;

            if (_playerMarker == null)
            {
                _playerMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _playerMarker.name = "MinimapPlayer";
                _playerMarker.layer = _layer;
                _playerMarker.transform.localScale = Vector3.one * 2.5f;
                var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                _playerDotMaterial = new Material(sh) { color = Color.blue };
                _playerMarker.GetComponent<MeshRenderer>().sharedMaterial = _playerDotMaterial;
                Object.Destroy(_playerMarker.GetComponent<SphereCollider>());
            }
            _playerMarker.transform.position = new Vector3(world.x, 1f, world.z);

            _mapUpdateTimer += Time.deltaTime;
            if (_mapUpdateTimer >= centerUpdateInterval)
            {
                _mapUpdateTimer = 0f;
                _map.UpdateMap(new Mapbox.Utils.Vector2d(userLoc.Latitude, userLoc.Longitude), _map.Zoom);
            }
        }

        public void Dispose()
        {
            ClearRoute();
            if (_playerMarker != null) Object.Destroy(_playerMarker);
            if (_playerDotMaterial != null) Object.Destroy(_playerDotMaterial);
        }
    }
}
