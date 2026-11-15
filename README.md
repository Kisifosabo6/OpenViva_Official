
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
* **Character IP, Meshes & Voice Lines (Shinobu / Monogatari):**
  * *Type:* Legacy fan-game assets extracted from older Viva Project builds (`Assets/Imported/Shinobu`, character audio clips).
  * *TODO: Fetching/building free replacements* (original community 3D character models, custom clothing cards, and open-source voice packs).
* **Level & Prefab Painting Tooling (nTools / Prefab Painter):**
  * *Type:* Legacy commercial editor tool leftovers.
  * *TODO: Fetching/building free replacements* (Unity native Polybrush, Terrain Tools, and open-source editor utilities).

## Copyrights

This repository is based on original Viva source code. Sir Hal shared
it publicly and gave us permissions to do whatever we want with it.
We decided to republish it under MIT licence. This repository does not
include assets from Unity Asset Store that were present in original source
code. We plan to find free alternatives for these assets or create our own.
