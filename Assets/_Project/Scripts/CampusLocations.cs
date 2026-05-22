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
            Locations.Clear();

            // Requested shortlist only (12 places) — coordinates from Google Maps pins
            Locations.Add(new CampusLocation { Name = "Barrier 3 PIEAS",            Description = "Campus entry barrier",                 Coordinates = new Vector2d(33.656947844635056, 73.27455426852424) });
            Locations.Add(new CampusLocation { Name = "PIEAS Reception",            Description = "Reception / admin desk",               Coordinates = new Vector2d(33.65676923705645, 73.26622869205573) });
            Locations.Add(new CampusLocation { Name = "B-Block",                    Description = "Academic Block B",                     Coordinates = new Vector2d(33.65613963961241, 73.26599371555247) });
            Locations.Add(new CampusLocation { Name = "C-Block",                    Description = "Academic Block C",                     Coordinates = new Vector2d(33.655746595197066, 73.26570472297307) });
            Locations.Add(new CampusLocation { Name = "Multi purpose Hall PIEAS",   Description = "Multi-purpose hall",                   Coordinates = new Vector2d(33.65336630223781, 73.26943710884538) });
            Locations.Add(new CampusLocation { Name = "D-Block",                    Description = "Academic Block D",                     Coordinates = new Vector2d(33.655349067679886, 73.26566307382723) });
            Locations.Add(new CampusLocation { Name = "A-block",                    Description = "Academic / administration block",      Coordinates = new Vector2d(33.65559324212741, 73.2647326531257) });
            Locations.Add(new CampusLocation { Name = "Auditorium",                 Description = "Inaam-ur-Rehman Auditorium",           Coordinates = new Vector2d(33.6557773477474, 73.2679828659049) });
            Locations.Add(new CampusLocation { Name = "DNE",                        Description = "Department of Nuclear Engineering",    Coordinates = new Vector2d(33.65444896013717, 73.26342665040356) });
            Locations.Add(new CampusLocation { Name = "PIEAS Central Library",      Description = "Main campus library",                  Coordinates = new Vector2d(33.65544695614308, 73.26699995573584) });
            Locations.Add(new CampusLocation { Name = "Computer Center",            Description = "Computer Center",                      Coordinates = new Vector2d(33.65520753418461, 73.26643986560083) });
            Locations.Add(new CampusLocation { Name = "Cafe PIEAS",                 Description = "Campus cafe / cafeteria",              Coordinates = new Vector2d(33.655072789201384, 73.26576592638742) });
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
