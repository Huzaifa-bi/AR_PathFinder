
# AR PathFinder — PIEAS Campus Navigation

An **augmented reality** campus navigation app for **PIEAS** (Pakistan Institute of Engineering and Applied Sciences). Walk through campus while AR arrows overlaid on the real world guide you turn-by-turn to your destination.

![AR PathFinder Demo](https://media.giphy.com/media/v1.Y2lkPTc5MGI3NjExZXFtb2NwNGxleHNqb29rcm8zZDF5ZWxscnZuMmdyMTBpaGJ0cHlnMyZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/QmHmjEwtbQtlkOx8T1/giphy.gif)

---

## ✨ Features

| Feature | Description |
|---|---|
| **AR Turn-by-Turn Navigation** | 3D arrows and route line overlaid on the camera feed guide you to any campus building |
| **Campus-Specific Pathfinding** | Custom walkway graph with A* algorithm — uses actual pedestrian paths, not road data |
| **Google Maps–style UI** | Dark-themed search screen with live filtering, distance badges, and building icons |
| **Turn Instruction Banner** | Direction arrow + text + distance to next turn, just like Google Maps |
| **Minimap PIP** | Picture-in-picture Mapbox map with route overlay and compass-aligned rotation |
| **Voice Navigation (TTS)** | Android TextToSpeech cues: "In 50 meters, turn right toward Library" |
| **Haptic Feedback** | Vibration pulses at waypoints and a double-buzz on arrival |
| **Animated AR Chevrons** | Pulsing 3D floor arrows along the route showing walking direction |
| **Off-route Recalculation** | Auto-recalculates if you wander >20m off the path |
| **Battery Optimization** | Throttles GPS and map updates when you stop walking |

---

## 🏫 Supported Locations (PIEAS Campus)

- C-Block
- D-Block
- PIEAS Central Library
- Inaam-ur-Rehman Auditorium
- Department of Nuclear Engineering (DNE)

> More locations can be added by editing `CampusLocations.cs` and `CampusPathGraph.cs`.

---

## 🚀 Getting Started

### Prerequisites

| Tool | Version |
|---|---|
| Unity | 2020.3.26f1+ (LTS recommended) |
| ARCore XR Plugin | 4.1.13+ |
| AR Foundation | 4.1.7+ |
| Mapbox Unity SDK | Included in project |
| TextMeshPro | 3.0.6+ (import Essential Resources) |
| Target Platform | Android (ARCore-compatible device) |

### Installation

1. **Clone** this repository:
   ```bash
   git clone https://github.com/Huzaifa-bi/AR_PathFinder.git
   ```
2. **Open** in Unity (2020.3.26f1 or newer).
3. **Import TMP Essential Resources**: `Window → TextMeshPro → Import TMP Essential Resources`.
4. **Set your Mapbox token**:
   - Create `Assets/StreamingAssets/Secrets.json`:
     ```json
     { "MapboxToken": "pk.YOUR_TOKEN_HERE" }
     ```
   - Or edit the fallback in `PIEASConfig.cs`.
5. **Build and deploy** the scene `Assets/_Project/Scenes/Mapbox AR Pathfinder` to an Android device.

### Testing in Unity Editor

The app uses a **Mock Location Provider** in the Editor, defaulting to the PIEAS campus center. You can press **L** to open the location changer for testing different starting positions.

---

## 📁 Project Structure

```
Assets/_Project/
├── Scripts/
│   ├── MenuController.cs          # Main controller: search, routing, navigation loop
│   ├── ARNavigationUI.cs          # Programmatic Canvas UI (Google Maps style)
│   ├── CampusLocations.cs         # Campus location definitions
│   ├── CampusPathGraph.cs         # Custom A* pathfinding on walkway graph
│   ├── AnimatedChevrons.cs        # Pulsing 3D chevron arrows on AR route
│   ├── HapticFeedbackSystem.cs    # Android vibration feedback
│   ├── NavigationAudioSystem.cs   # Android TTS voice cues
│   ├── PIEASConfig.cs             # Centralized config (token, colors, bounds)
│   ├── DirectionsFactory.cs       # Mapbox Directions API integration
│   ├── LineBuilder.cs             # Minimap route line mesh builder
│   ├── TestCameraController.cs    # Editor/device testing camera + GPS
│   └── LocationChanger.cs         # Debug: change mock GPS position
└── Scenes/
    └── Mapbox AR Pathfinder.unity
```

---

## 🔧 Configuration

All PIEAS-specific settings are centralized in [`PIEASConfig.cs`](Assets/_Project/Scripts/PIEASConfig.cs):

- Campus bounds (NE/SW corners)
- Map center coordinates and zoom level
- Branding colors
- Mapbox API token (loaded from `Secrets.json`)

---

## 📖 How It Works

1. **Search**: User types or taps a campus location from the card list.
2. **Pathfinding**: The app first tries the custom `CampusPathGraph` (A* on real walkways). If both points are on campus, it produces a pedestrian-friendly path. Otherwise, it falls back to the Mapbox Directions API.
3. **AR Rendering**: `MapboxRoute` places 3D signpost and path renderer objects at GPS locations using AR Foundation's `PlaceAtLocation`.
4. **Live Navigation**: Every 0.5s (or 2s when stationary), the app checks user GPS against the route, updates the turn banner, fires voice/haptic cues, and detects arrival or off-route conditions.

---

## 🤝 Contributors

[![Huzaifa](https://img.shields.io/badge/Huzaifa_Bilal-0AF?style=for-the-badge&logo=ReverbNation&logoColor=White)](https://github.com/Huzaifa-bi)

### Original Mapbox AR Pathfinder

[![Aby Stalin](https://img.shields.io/badge/Aby_Stalin-0AF?style=for-the-badge&logo=ReverbNation&logoColor=White)](https://github.com/Alby0n)
[![Nadeem](https://img.shields.io/badge/Nadeem_MOHAMMED-000?style=for-the-badge&logo=Starship&logoColor=red)](https://github.com/nadeem100au)
[![Rizwan](https://img.shields.io/badge/N_Rizwan-E23?style=for-the-badge&logo=1001Tracklists&logoColor=black)](https://github.com/StuntStorm)

---

## 📄 License

This project is licensed under the MIT License — see [LICENSE](LICENSE) for details.
