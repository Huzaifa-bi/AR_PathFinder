using System.Collections.Generic;
using UnityEngine;
using Mapbox.Utils;

//This script defines campus-specific locations for quick navigation
namespace ARLocation.MapboxRoutes.SampleProject
{
    [System.Serializable]
    public class CampusLocation
    {
        public string Name;
        public string Description;
        public Vector2d Coordinates;
    }

    public class CampusLocations : MonoBehaviour
    {
        public List<CampusLocation> Locations = new List<CampusLocation>();

        private static CampusLocations _instance;

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                InitializeDefaultLocations();
            }
        }

        private void InitializeDefaultLocations()
        {
            // PIEAS Campus Locations
            Locations.Add(new CampusLocation
            {
                Name = "C-block",
                Description = "PIEAS C Block",
                Coordinates = new Vector2d(33.65578597201986, 73.26552018567683)
            });

            Locations.Add(new CampusLocation
            {
                Name = "D-block",
                Description = "PIEAS D Block",
                Coordinates = new Vector2d(33.65533195716392, 73.26561587673456)
            });

            Locations.Add(new CampusLocation
            {
                Name = "PIEAS Central Library",
                Description = "Library",
                Coordinates = new Vector2d(33.6554567451093, 73.26708313965757)
            });

            Locations.Add(new CampusLocation
            {
                Name = "Auditorium",
                Description = "Inaam-ur-Rehman Auditorium",
                Coordinates = new Vector2d(33.655887550014555, 73.26772910917398)
            });

            Locations.Add(new CampusLocation
            {
                Name = "DNE",
                Description = "Department Nuclear Engineering",
                Coordinates = new Vector2d(33.654431025749346, 73.26334063974608)
            });
        }

        public static CampusLocations Instance => _instance;

        public CampusLocation GetLocationByName(string name)
        {
            return Locations.Find(loc => loc.Name == name);
        }

        public List<CampusLocation> GetAllLocations()
        {
            return Locations;
        }
    }
}
