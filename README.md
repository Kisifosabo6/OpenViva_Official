
# OpenViva

Viva, formally OpenViva, is an open-source continuation of Viva Project, originally made by Sir Hal. It is a VR and non-VR compatible game where you can interact with your very own AI anime character. The game features advanced AI simulation that can interact and respond to many of your actions in a dynamic way through inverse kinematics, complex behaviors, and currently over 200 animations. The character's mood is dynamic and responds to how you treat it, allowing you to play with it, feed it, or just be friends with it.

# Notice
We want to get the ball rolling again. In order to do so, we made this new repository to get ourselves organized and increase productivity. This should also help us avoid confusion.
This repository was made after the V0.3.0 update.

Versions going past 0.3.0 are based upon a complete rewrite of the game using the original assets of the older Viva Project plus a few new ones custom made specifically for this game. 

# Community
We are the OpenViva team, a group dedicated to continuing Viva project as a free and open-source game under the name Viva.
 
Our goal is to polish 0.8.03 and replace any and all third-party paid assets with either free or custom assets. All assets and scripts are available to anyone who wants to download them or fork the project on our GitHub. 

If you have any questions or want to help us, you can join our community on [Discord](https://discord.com/invite/w7rFnssghW). To get character cards and clothing, please visit our [Website](https://viva-project.org).

# Developers Wanted
We are always looking for more developers to join our team and help us improve the game. If you have the skills and are interested in contributing, please contact us on our Discord.

# Localization
Note that localization is currently disabled due to the rewrite. For now, all that can be used in English.
We currently have translations for:
- English
- Russian
- Hungarian 
- Turkish
- Romanian
- German
- Slovakian
- Spanish (Latin American)
- Spanish (Spain)
- French 


# Prerequisites for Development

Required:
- Unity 6000.3.22f1

Recommended:
- Visual Studio (2022 or 2026) or any other C# IDE (Programming)
- Visual Studio Git or GitHub Desktop (For better Git management)
- Blender (3D modeling)
- Krita (2D art)

### Third-Party & Restricted Dependencies
The following dependencies are third-party paid, proprietary, or subject to redistribution restrictions (such as the Unity Asset Store EULA or character IP) that cannot be freely shared:

* **Cloth & Dynamic Physics Simulation (Magica Cloth 2 / Dynamic Bone):**
  * *Type:* Commercial Unity Asset Store package. Excluded from repository tracking.
  * *TODO: Fetching/building free replacements* (e.g., native Unity Cloth or open-source job-based spring bone physics).
* **Skybox Texture Pack (`Assets/Plugins/Fantasy Skybox FREE`):**
  * *Type:* Unity Asset Store EULA asset (redistribution restricted even if free on store).
  * *TODO: Fetching/building free replacements* (procedural day-night cycle shaders / CC0 public domain textures).
* **Character IP, Meshes & Voice Lines:**
  * *Type:* Legacy fan-game assets extracted from older builds or third-party franchises. Excluded from the repository (see detailed IP notice below).
  * *TODO: Fetching/building free replacements* (original community 3D character models, custom clothing cards, and open-source voice packs).
* **Level & Prefab Painting Tooling (nTools / Prefab Painter):**
  * *Type:* Legacy commercial editor tool leftovers.
  * *TODO: Fetching/building free replacements* (Unity native Polybrush, Terrain Tools, and open-source editor utilities).

## Fork Enhancements & Detailed Changes (vs. Upstream `dev`)

This fork focuses on stabilizing the project for open-source development, removing commercial asset dependencies, enhancing desktop interaction, and overhauling environmental immersion. Below is a module-by-module breakdown of key differences compared to upstream:

### 1. UI & Desktop Player Interaction
* **`Assets/Scripts/UI/PauseMenu.cs` & Book Prefabs:**
  * **Dynamic Page Bone Binding:** Interactive UI page canvases (`LeftPage` / `RightPage`) are now dynamically parented directly to armature leaf bones (`book_r/UI_l` and `book_l/UI_r`) with configurable local offset vectors and Euler angles.
  * **Desktop Camera Alignment:** Added desktop closer-placement options and automatic `GraphicRaycaster` initialization across all canvas pages to prevent hands from obstructing mouse raycasts.
* **`Assets/Scripts/PlatformDependant/Desktop/PlayerKB_GrabObject.cs`:**
  * Refined desktop item-grabbing mechanics, object rotation controls, and raycast hit detection for keyboard/mouse gameplay.
* **`Assets/Scripts/Player/PlayerSwitchInputMode.cs`:**
  * Stabilized hot-swapping between VR and Desktop input modes without resetting the player camera or breaking interactable references.

### 2. Proprietary Asset Decoupling & Build Fixes
* **`Assets/Scripts/Characters/Testing/PhysicsAttacher.cs`:**
  * Decoupled runtime bone physics from the proprietary `MagicaCloth2` package. The system now checks for package presence and logs clean fallbacks, allowing the project to compile and run in pure open-source environments.
* **`Assets/Scripts/PlatformDependant/Desktop/Minigame/FruitCutMinigame.cs`:**
  * Stripped dead references to the commercial `nTools.PrefabPainter` editor tool.
* **Cleanup of Commercial Leftovers:**
  * Removed obsolete `PrefabPainterSettings` scripts and broken asset meta files that caused compiler errors on fresh clones.
* **Compiler Warning Suppression (`Assets/csc.rsp` & `.editorconfig`):**
  * Configured project-wide compiler rulesets to suppress non-critical domain reload warnings (`UDR0001` through `UDR0005`) from Unity Project Auditor.

### 3. Rendering, Skybox & Day-Night Simulation
* **`Assets/Scripts/World/DayNightCycle.cs` & `DaylightSensor.cs`:**
  * Implemented smooth orbital calculations for celestial bodies (sun and moon) and multi-phase skybox blending across time transitions (`Dawn`, `Morning`, `Day`, `Afternoon`, `Dusk`, `Night`).
* **Custom Shaders (`Assets/Shaders/Sky/StylizedMoon.shader`):**
  * Added stylized celestial moon and star shaders with procedural phase shading.
* **Water & Lighting Polish:**
  * Tuned `PlanarReflection.cs` and `UWa-Template-Anime.mat` for stylized water reflections.
  * Fine-tuned `QualitySettings.asset` with 4x MSAA anti-aliasing, optimized shadow normal bias (0.4), and vSync adjustments for smooth high-refresh VR/desktop playback.

### 4. Audio & Ambiance
* **`Assets/Scripts/Audio/Ambiance/DynamicAmbianceController.cs` & `StaticAmbianceManager.cs`:**
  * Dynamic environmental sound mixing reacting to indoor/outdoor triggers and time-of-day transitions.
* **Steam Audio Integration (`Assets/Plugins/SteamAudio/`):**
  * Configured binaural spatial listener settings for accurate 3D directional sound simulation.

### 5. Companion Prefab & Scene Hygiene
* **`Assets/Scenes/MainScene.unity` & `Landscape_Main.unity`:**
  * Cleaned up dangling scene components, sanitized missing script references, and synchronized environment lighting with the updated day-night cycle.


## Licensing & Intellectual Property (IP)

### Source Code
This repository is based on the original Viva source code shared publicly by Sir Hal with permission to continue development. The source code, custom tools, and scripts developed by the OpenViva community are released under the [MIT License](LICENSE).

### Character IP, Companion Models & Commercial Use Disclaimer
While this repository is freely distributed under the **MIT License**, the MIT license inherently grants downstream developers the freedom to modify, redistribute, and even utilize the software for **commercial purposes**. 

Because of this legal scope, **copyrighted character IP, meshes, models, and voice lines are intentionally omitted from this repository**:

1. **Incompatible with Permissive Licensing:** Third-party character designs, likenesses, meshes, and voice clips, such as those inspired by or extracted from copyrighted franchises are protected intellectual property. They cannot legally be re-licensed under an open-source permissive license such as MIT, nor can they be conveyed for commercial redistribution.
2. **Preventing Downstream Legal Conflicts:** If copyrighted character assets were bundled directly into the MIT-licensed repository, downstream developers or creators utilizing the project under MIT terms could unknowingly face trademark and copyright infringement liabilities.
3. **Framework-First Architecture:** OpenViva is designed as a modular character AI and interaction engine. The repository contains the core inverse kinematics, behavior state machines, shaders, and interaction mechanics. Developers and players are expected to provide their own original humanoid models (e.g., custom VRoid models, original Blender rigs) and voice packs.
4. **Commercial Compliance Notice:** Anyone forking, adapting, or creating commercial products based on OpenViva is strictly responsible for providing 100% original, appropriately licensed, or royalty-free 3D character models, textures, animations, audio, and plugins.

