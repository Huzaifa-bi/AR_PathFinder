using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Custom campus pathfinding graph for PIEAS campus.
/// Since Mapbox doesn't have internal campus footpaths, this graph defines
/// actual walkway nodes and edges, and uses A* to find shortest walking paths.
/// 
/// HOW TO ADD/EDIT PATHS:
/// 1. Add new PathNode entries in InitializeGraph() with GPS coordinates
/// 2. Add edges between connected nodes using AddEdge()
/// 3. The A* algorithm will automatically find shortest paths through the graph
/// </summary>
namespace ARLocation.MapboxRoutes.SampleProject
{
    public class CampusPathGraph
    {
        // ============================================================
        //  Data Structures
        // ============================================================

        public class PathNode
        {
            public string Id;
            public string Name;
            public double Latitude;
            public double Longitude;
            public List<PathEdge> Edges = new List<PathEdge>();

            public PathNode(string id, string name, double lat, double lon)
            {
                Id = id;
                Name = name;
                Latitude = lat;
                Longitude = lon;
            }
        }

        public class PathEdge
        {
            public PathNode Target;
            public double Distance; // in meters

            public PathEdge(PathNode target, double distance)
            {
                Target = target;
                Distance = distance;
            }
        }

        // A* internal node for priority queue
        private class AStarNode
        {
            public PathNode Node;
            public AStarNode Parent;
            public double GCost; // actual cost from start
            public double HCost; // heuristic cost to end
            public double FCost => GCost + HCost;
        }

        // ============================================================
        //  Graph Data
        // ============================================================

        private Dictionary<string, PathNode> nodes = new Dictionary<string, PathNode>();
        private static CampusPathGraph _instance;

        public static CampusPathGraph Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CampusPathGraph();
                    _instance.InitializeGraph();
                }
                return _instance;
            }
        }

        // ============================================================
        //  Graph Construction
        // ============================================================

        private PathNode AddNode(string id, string name, double lat, double lon)
        {
            var node = new PathNode(id, name, lat, lon);
            nodes[id] = node;
            return node;
        }

        private void AddEdge(string fromId, string toId)
        {
            if (!nodes.ContainsKey(fromId) || !nodes.ContainsKey(toId))
            {
                Debug.LogError($"[CampusPathGraph] Cannot add edge: node not found ({fromId} -> {toId})");
                return;
            }

            var from = nodes[fromId];
            var to = nodes[toId];
            double dist = HaversineDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude);

            // Bidirectional edges (walkways go both ways)
            from.Edges.Add(new PathEdge(to, dist));
            to.Edges.Add(new PathEdge(from, dist));
        }

        /// <summary>
        /// Initialize the campus walkway graph with actual PIEAS campus paths.
        /// 
        /// Node naming convention:
        ///   - "dest_xxx"  = destination buildings (from CampusLocations)
        ///   - "junc_xxx"  = walkway junctions/intersections
        ///   - "path_xxx"  = intermediate points along walkways
        ///
        /// IMPORTANT: These coordinates should be adjusted to match the actual
        /// walkway positions on campus. Use Google Maps satellite view to verify.
        /// </summary>
        private void InitializeGraph()
        {
            // ============================================================
            //  DESTINATION NODES (match CampusLocations.cs exactly)
            // ============================================================
            AddNode("dest_cblock",     "C-Block",          33.65578597201986,  73.26552018567683);
            AddNode("dest_dblock",     "D-Block",          33.65533195716392,  73.26561587673456);
            AddNode("dest_library",    "Central Library",   33.6554567451093,   73.26708313965757);
            AddNode("dest_auditorium", "Auditorium",        33.655887550014555, 73.26772910917398);
            AddNode("dest_dne",        "DNE",               33.654431025749346, 73.26334063974608);

            // ============================================================
            //  CENTRAL HUB - MAATI Chowk area (main campus intersection)
            // ============================================================
            AddNode("junc_maati",      "MAATI Chowk",       33.65585, 73.26550);
            AddNode("junc_center",     "Campus Center",     33.65598735240187, 73.2649697331715);

            // ============================================================
            //  WALKWAY JUNCTION NODES (intersections of footpaths)
            // ============================================================
            
            // North-south spine walkway (connects C-Block area to D-Block area)
            AddNode("junc_ns_1",       "NS Junction North", 33.65575, 73.26565);
            AddNode("junc_ns_2",       "NS Junction Mid",   33.65555, 73.26565);
            AddNode("junc_ns_3",       "NS Junction South", 33.65540, 73.26565);

            // East-west walkway (connects center to Library)
            AddNode("junc_ew_1",       "EW Junction West",  33.65565, 73.26600);
            AddNode("junc_ew_2",       "EW Junction Mid",   33.65560, 73.26640);
            AddNode("junc_ew_3",       "EW Junction East",  33.65555, 73.26680);

            // Library to Auditorium walkway
            AddNode("junc_lib_aud_1",  "Lib-Aud Path 1",    33.65560, 73.26720);
            AddNode("junc_lib_aud_2",  "Lib-Aud Path 2",    33.65575, 73.26750);

            // Western walkway (connects center to DNE area)
            AddNode("junc_west_1",     "West Path 1",       33.65570, 73.26480);
            AddNode("junc_west_2",     "West Path 2",       33.65540, 73.26440);
            AddNode("junc_west_3",     "West Path 3",       33.65510, 73.26400);
            AddNode("junc_west_4",     "West Path 4",       33.65475, 73.26370);

            // South connector (D-Block area to DNE side)
            AddNode("junc_south_1",    "South Path 1",      33.65500, 73.26530);
            AddNode("junc_south_2",    "South Path 2",      33.65480, 73.26490);

            // ============================================================
            //  EDGES (walkway connections)
            // ============================================================

            // --- Central hub connections ---
            AddEdge("junc_center", "junc_maati");
            AddEdge("junc_center", "junc_west_1");
            AddEdge("junc_maati",  "dest_cblock");
            AddEdge("junc_maati",  "junc_ns_1");

            // --- North-South spine ---
            AddEdge("junc_ns_1", "junc_ns_2");
            AddEdge("junc_ns_2", "junc_ns_3");
            AddEdge("junc_ns_1", "dest_cblock");
            AddEdge("junc_ns_3", "dest_dblock");

            // --- East-West walkway to Library ---
            AddEdge("junc_ns_2",  "junc_ew_1");
            AddEdge("junc_ew_1",  "junc_ew_2");
            AddEdge("junc_ew_2",  "junc_ew_3");
            AddEdge("junc_ew_3",  "dest_library");
            AddEdge("junc_maati", "junc_ew_1");

            // --- Library to Auditorium ---
            AddEdge("dest_library",   "junc_lib_aud_1");
            AddEdge("junc_lib_aud_1", "junc_lib_aud_2");
            AddEdge("junc_lib_aud_2", "dest_auditorium");

            // --- Western path to DNE ---
            AddEdge("junc_west_1", "junc_west_2");
            AddEdge("junc_west_2", "junc_west_3");
            AddEdge("junc_west_3", "junc_west_4");
            AddEdge("junc_west_4", "dest_dne");

            // --- South connectors ---
            AddEdge("junc_ns_3",    "junc_south_1");
            AddEdge("junc_south_1", "junc_south_2");
            AddEdge("junc_south_2", "junc_west_2");
            AddEdge("dest_dblock",  "junc_south_1");

            // --- Direct shortcuts (diagonal footpaths visible in satellite) ---
            AddEdge("dest_cblock", "junc_ew_1");  // C-Block to east walkway
            AddEdge("junc_ns_3",   "junc_ew_2");  // South spine to mid-east
        }

        // ============================================================
        //  Pathfinding (A*)
        // ============================================================

        /// <summary>
        /// Find the shortest path between two GPS coordinates using A*.
        /// Returns a list of PathNodes from start to end, or null if no path exists.
        /// </summary>
        public List<PathNode> FindPath(double startLat, double startLon, double endLat, double endLon)
        {
            // Find closest graph nodes to start and end coordinates
            var startNode = FindClosestNode(startLat, startLon);
            var endNode = FindClosestNode(endLat, endLon);

            if (startNode == null || endNode == null)
            {
                Debug.LogWarning("[CampusPathGraph] Could not find start or end node");
                return null;
            }

            Debug.Log($"[CampusPathGraph] Finding path from {startNode.Name} to {endNode.Name}");

            // A* algorithm
            var openSet = new List<AStarNode>();
            var closedSet = new HashSet<string>();

            openSet.Add(new AStarNode
            {
                Node = startNode,
                Parent = null,
                GCost = 0,
                HCost = HaversineDistance(startNode.Latitude, startNode.Longitude, endNode.Latitude, endNode.Longitude)
            });

            while (openSet.Count > 0)
            {
                // Find node with lowest F cost
                int bestIndex = 0;
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (openSet[i].FCost < openSet[bestIndex].FCost ||
                        (openSet[i].FCost == openSet[bestIndex].FCost && openSet[i].HCost < openSet[bestIndex].HCost))
                    {
                        bestIndex = i;
                    }
                }

                var current = openSet[bestIndex];
                openSet.RemoveAt(bestIndex);

                // Reached destination
                if (current.Node.Id == endNode.Id)
                {
                    return ReconstructPath(current, startLat, startLon, endLat, endLon);
                }

                closedSet.Add(current.Node.Id);

                // Explore neighbors
                foreach (var edge in current.Node.Edges)
                {
                    if (closedSet.Contains(edge.Target.Id)) continue;

                    double tentativeG = current.GCost + edge.Distance;

                    // Check if already in open set with better cost
                    var existing = openSet.Find(n => n.Node.Id == edge.Target.Id);
                    if (existing != null)
                    {
                        if (tentativeG < existing.GCost)
                        {
                            existing.GCost = tentativeG;
                            existing.Parent = current;
                        }
                        continue;
                    }

                    openSet.Add(new AStarNode
                    {
                        Node = edge.Target,
                        Parent = current,
                        GCost = tentativeG,
                        HCost = HaversineDistance(edge.Target.Latitude, edge.Target.Longitude, endNode.Latitude, endNode.Longitude)
                    });
                }
            }

            Debug.LogWarning("[CampusPathGraph] No path found!");
            return null;
        }

        /// <summary>
        /// Reconstruct the path from A* result, including the actual start/end GPS positions
        /// (not just the nearest graph nodes).
        /// </summary>
        private List<PathNode> ReconstructPath(AStarNode endAStarNode, double startLat, double startLon, double endLat, double endLon)
        {
            var path = new List<PathNode>();

            var current = endAStarNode;
            while (current != null)
            {
                path.Add(current.Node);
                current = current.Parent;
            }

            path.Reverse();

            // Prepend actual start position if it's different from the first graph node
            var first = path[0];
            double distToFirst = HaversineDistance(startLat, startLon, first.Latitude, first.Longitude);
            if (distToFirst > 5) // more than 5 meters away
            {
                path.Insert(0, new PathNode("start", "Start", startLat, startLon));
            }

            // Append actual end position if it's different from the last graph node
            var last = path[path.Count - 1];
            double distToLast = HaversineDistance(endLat, endLon, last.Latitude, last.Longitude);
            if (distToLast > 5)
            {
                path.Add(new PathNode("end", "Destination", endLat, endLon));
            }

            Debug.Log($"[CampusPathGraph] Path found with {path.Count} waypoints");
            return path;
        }

        // ============================================================
        //  Utilities
        // ============================================================

        /// <summary>
        /// Find the closest graph node to the given GPS coordinates.
        /// </summary>
        public PathNode FindClosestNode(double lat, double lon)
        {
            PathNode closest = null;
            double minDist = double.MaxValue;

            foreach (var kvp in nodes)
            {
                double dist = HaversineDistance(lat, lon, kvp.Value.Latitude, kvp.Value.Longitude);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = kvp.Value;
                }
            }

            return closest;
        }

        /// <summary>
        /// Haversine formula to calculate distance between two GPS points in meters.
        /// </summary>
        public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000; // Earth radius in meters
            double dLat = (lat2 - lat1) * System.Math.PI / 180.0;
            double dLon = (lon2 - lon1) * System.Math.PI / 180.0;
            double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                       System.Math.Cos(lat1 * System.Math.PI / 180.0) *
                       System.Math.Cos(lat2 * System.Math.PI / 180.0) *
                       System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
            double c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));
            return R * c;
        }

        /// <summary>
        /// Calculate bearing from point 1 to point 2 in degrees (0-360).
        /// </summary>
        public static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
        {
            double dLon = (lon2 - lon1) * System.Math.PI / 180.0;
            double lat1Rad = lat1 * System.Math.PI / 180.0;
            double lat2Rad = lat2 * System.Math.PI / 180.0;

            double y = System.Math.Sin(dLon) * System.Math.Cos(lat2Rad);
            double x = System.Math.Cos(lat1Rad) * System.Math.Sin(lat2Rad) -
                       System.Math.Sin(lat1Rad) * System.Math.Cos(lat2Rad) * System.Math.Cos(dLon);
            double bearing = System.Math.Atan2(y, x) * 180.0 / System.Math.PI;
            return (bearing + 360) % 360;
        }

        /// <summary>
        /// Get the turn instruction based on the angle between two bearings.
        /// </summary>
        public static string GetTurnInstruction(double bearingBefore, double bearingAfter, string streetName)
        {
            double angle = bearingAfter - bearingBefore;
            if (angle < 0) angle += 360;
            if (angle > 360) angle -= 360;

            string direction;
            if (angle < 30 || angle > 330)
                direction = "Continue straight";
            else if (angle >= 30 && angle < 80)
                direction = "Turn slight right";
            else if (angle >= 80 && angle < 120)
                direction = "Turn right";
            else if (angle >= 120 && angle < 170)
                direction = "Turn sharp right";
            else if (angle >= 170 && angle < 190)
                direction = "Make a U-turn";
            else if (angle >= 190 && angle < 240)
                direction = "Turn sharp left";
            else if (angle >= 240 && angle < 300)
                direction = "Turn left";
            else
                direction = "Turn slight left";

            if (!string.IsNullOrEmpty(streetName) && streetName != "Start" && streetName != "Destination")
                return $"{direction} toward {streetName}";
            return direction;
        }

        /// <summary>
        /// Convert a list of PathNodes into a RouteResponse compatible with the existing MapboxRoute system.
        /// </summary>
        public static RouteResponse ConvertToRouteResponse(List<PathNode> path)
        {
            if (path == null || path.Count < 2) return null;

            var response = new RouteResponse();
            response.Code = "Ok";
            response.routes = new List<Route>();
            response.waypoints = new List<Waypoint>();

            var route = ScriptableObject.CreateInstance<Route>();

            // Build geometry (all coordinates along the path)
            route.geometry = new Route.Geometry();
            route.geometry.type = "LineString";
            foreach (var node in path)
            {
                route.geometry.coordinates.Add(new Location(node.Latitude, node.Longitude, 0));
            }

            // Calculate total distance
            double totalDistance = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                totalDistance += HaversineDistance(
                    path[i].Latitude, path[i].Longitude,
                    path[i + 1].Latitude, path[i + 1].Longitude);
            }
            route.distance = (float)totalDistance;

            // Build leg with steps (one step per node = one maneuver point)
            var leg = new Route.RouteLeg();
            leg.distance = (float)totalDistance;
            leg.steps = new List<Route.Step>();

            for (int i = 0; i < path.Count; i++)
            {
                var step = new Route.Step();
                step.name = path[i].Name;

                // Step geometry: current point to next point
                step.geometry = new Route.Geometry();
                step.geometry.type = "LineString";
                step.geometry.coordinates.Add(new Location(path[i].Latitude, path[i].Longitude, 0));
                if (i < path.Count - 1)
                {
                    step.geometry.coordinates.Add(new Location(path[i + 1].Latitude, path[i + 1].Longitude, 0));
                    step.distance = (float)HaversineDistance(
                        path[i].Latitude, path[i].Longitude,
                        path[i + 1].Latitude, path[i + 1].Longitude);
                }
                else
                {
                    step.distance = 0;
                }

                // Maneuver
                step.maneuver = new Route.Maneuver();
                step.maneuver.location = new Location(path[i].Latitude, path[i].Longitude, 0);

                if (i == 0)
                {
                    step.maneuver.type = "depart";
                    step.maneuver.instruction = "Start walking";
                    step.maneuver.bearing_before = 0;
                    step.maneuver.bearing_after = (i < path.Count - 1) ?
                        (int)CalculateBearing(path[i].Latitude, path[i].Longitude, path[i + 1].Latitude, path[i + 1].Longitude) : 0;
                }
                else if (i == path.Count - 1)
                {
                    step.maneuver.type = "arrive";
                    step.maneuver.instruction = $"You have arrived at {path[i].Name}";
                    step.maneuver.bearing_before = (int)CalculateBearing(
                        path[i - 1].Latitude, path[i - 1].Longitude,
                        path[i].Latitude, path[i].Longitude);
                    step.maneuver.bearing_after = 0;
                }
                else
                {
                    double bearingBefore = CalculateBearing(
                        path[i - 1].Latitude, path[i - 1].Longitude,
                        path[i].Latitude, path[i].Longitude);
                    double bearingAfter = CalculateBearing(
                        path[i].Latitude, path[i].Longitude,
                        path[i + 1].Latitude, path[i + 1].Longitude);

                    step.maneuver.type = "turn";
                    step.maneuver.instruction = GetTurnInstruction(bearingBefore, bearingAfter, path[i].Name);
                    step.maneuver.bearing_before = (int)bearingBefore;
                    step.maneuver.bearing_after = (int)bearingAfter;
                }

                leg.steps.Add(step);
            }

            route.legs = new List<Route.RouteLeg> { leg };
            response.routes.Add(route);

            // Waypoints
            response.waypoints.Add(new Waypoint
            {
                name = path[0].Name,
                location = new Location(path[0].Latitude, path[0].Longitude, 0)
            });
            response.waypoints.Add(new Waypoint
            {
                name = path[path.Count - 1].Name,
                location = new Location(path[path.Count - 1].Latitude, path[path.Count - 1].Longitude, 0)
            });

            Debug.Log($"[CampusPathGraph] RouteResponse built: {path.Count} waypoints, {totalDistance:F0}m total");
            return response;
        }
    }
}
