# AR PathFinder - Final Completion To-Do List

This list outlines the remaining steps to bring the PIEAS Campus AR Navigation project to a production-ready state.

## 1. Core Functionality & Pathfinding
- [ ] **Expand Campus Graph**: Add missing nodes to `CampusPathGraph.cs` for every building entrance (e.g., specific departments, hostels, cafeterias).
- [ ] **Path Snapping Logic**: Refine the logic that snaps the user's starting GPS position to the nearest walkway node to prevent "straight-line" detours through buildings.
- [ ] **Dynamic Recalculation**: Ensure that if a user deviates more than 10 meters from the custom campus path, the route automatically recalculates from their new position.
- [ ] **Indoor/Outdoor Transition**: (Advanced) Add logic to handle cases where GPS signal might drop near large buildings.

## 2. User Interface (UI) Improvements
- [ ] **Polished Search Results**: Update the IMGUI list to show building icons or distances next to each search result.
- [ ] **Minimalist HUD**: Add a persistent "Distance to Destination" and "Estimated Time of Arrival (ETA)" overlay that stays visible during navigation without cluttering the AR view.
- [ ] **AR Status Indicator**: Add a small, non-intrusive icon to show if AR tracking is "Tracking", "Initializing", or "Lost".
- [ ] **Success Screen**: Create a dedicated "You Have Arrived" screen with options to "Exit" or "Search Again".

## 3. Audio & Feedback
- [ ] **Enable TTS Audio**: Uncomment and test the `NavigationAudioSystem.cs` calls in `MenuController.cs`.
- [ ] **Turn-by-Turn Voice**: Add voice prompts for approaching turns (e.g., "In 20 meters, turn right toward the Library").
- [ ] **Haptic Feedback**: Add subtle vibrations when the user reaches a waypoint or the final destination.

## 4. AR Visuals & Graphics
- [ ] **Chevron Indicators**: Ensure the 3D arrows on the floor are animated (e.g., pulsing or moving forward) to clearly indicate direction.
- [ ] **Occlusion Handling**: Use a shader that makes the path appear slightly different when it's "behind" a real-world building (if depth data is available).
- [ ] **Compass Alignment**: Double-check the alignment between the AR path and the real-world North to prevent "drifting" during long walks.

## 5. Deployment & Testing
- [ ] **Permission Handling**: Ensure the app asks for Camera and Fine Location permissions at runtime and provides a "Settings" link if they are denied.
- [ ] **Battery Optimization**: Throttle the Mapbox map update frequency when the user is stationary to save battery.
- [ ] **Campus Field Test**: Perform a full "stress test" by walking the entire perimeter of PIEAS and identifying any areas with poor GPS/AR tracking.
- [ ] **README Update**: Finalize the GitHub README with updated installation instructions and a video/GIF of the working UI.

## 6. Project Maintenance
- [ ] **Redact Mapbox Tokens**: Move tokens to a `Secrets.json` file or environment variables so they aren't committed to the public repo.
- [ ] **Clean Code**: Perform a final pass to remove unused scripts (like `ARNavigationUI.cs` if you decide to stick with IMGUI).
