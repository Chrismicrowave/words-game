# words

Unity word game project (GDS4 Game-a-Week, week 02).

## Tech Stack
- Unity (C#)
- TextMesh Pro for text rendering
- New Input System
- StandaloneFileBrowser plugin
- CRT-Free shader package

## Project Structure
- `Assets/-Scripts/` — Core game scripts (GameManager, AudioManager, CamShake, etc.)
- `Assets/-Anim/`, `-Audio/`, `-Images/`, `-Material/`, `-Prefabs/` — Game assets
- `Assets/Scenes/` — Unity scenes
- `Assets/CRT-Free/` — CRT post-processing effect
- `Assets/StandaloneFileBrowser/` — Native file dialog plugin

## Conventions
- Script folder uses dash prefix (`-Scripts`) for sorting at top of Assets
- C# scripts follow standard Unity MonoBehaviour patterns
