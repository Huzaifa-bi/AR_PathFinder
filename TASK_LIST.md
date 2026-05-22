# AR PathFinder - Final Completion To-Do List

## 1. Core Functionality & Pathfinding
- [x] **Path Snapping Logic**: A* pathfinder snaps to nearest graph node and prepends/appends actual GPS start/end.
- [x] **Dynamic Recalculation**: If user deviates >20m from route, auto-recalculates from current GPS position with voice cue.
- [ ] **Expand Campus Graph**: User will provide custom locations and coordinates.
- [ ] **Indoor/Outdoor Transition**: (Advanced) Handle GPS signal drops near large buildings.

## 2. User Interface (UI) Improvements
- [x] **Canvas-Based UI**: Replaced legacy IMGUI with Google Maps-style dark Canvas UI.
- [x] **Minimalist HUD**: Distance-to-Destination and ETA overlay visible during navigation.
- [x] **AR Status Indicator**: AR tracking status badge (green/yellow/red) on search screen.
- [x] **Success Screen**: "You Have Arrived" message with auto-dismiss.
- [x] **Turn Instruction Banner**: Top banner shows arrow + instruction + distance to next turn.
- [x] **Polished Search Results**: Building-type icons (📚🏢🎭) and distance badges next to each result.

## 3. Audio & Feedback
- [x] **Enable TTS Audio**: Route start and arrival audio active.
- [x] **Turn-by-Turn Voice**: Approaching turn (~50m), at turn (~8m), approaching destination (~30m), and recalculation voice prompts.
- [x] **Haptic Feedback**: Subtle vibrations at waypoints/destination (HapticFeedbackSystem integrated).

## 4. AR Visuals & Graphics
- [x] **Chevron Indicators**: Animated 3D floor arrows (pulsing/moving forward) via AnimatedChevrons.cs.
- [ ] **Occlusion Handling**: Path shader for behind-building rendering (requires ARKit/ARCore depth API).
- [x] **Compass Alignment**: Minimap rotation synced with device compass heading.

## 5. Deployment & Testing
- [x] **Permission Handling**: Camera + Fine Location runtime permissions.
- [x] **Mapbox Token**: Real API token set.
- [x] **Battery Optimization**: Throttle navigation updates when stationary (speed < 0.5m/s).
- [ ] **Campus Field Test**: Full perimeter stress test.
- [x] **README Update**: Installation instructions, feature table, project structure docs.

## 6. Project Maintenance
- [x] **Redact Mapbox Tokens**: Moved to Secrets.json with .gitignore entry + runtime loader in PIEASConfig.
- [x] **Clean Code**: ArMenuController.cs is legacy (kept for reference). All active scripts documented.
