# 2D Vertical Shooter

[Korean version (README.ko.md)](README.ko.md)

A Unity 2D vertical shooter prototype project.

This repository contains a playable sample scene where the player can move, shoot, and defeat spawning enemies. The current implementation focuses on core gameplay loop setup (movement, shooting, enemy spawn, hit detection, and basic feedback) and project configuration for Unity 6 + URP.

## Project Summary

- Genre: 2D vertical shooter
- Engine: Unity 6
- Render Pipeline: Universal Render Pipeline (URP)
- Input: Unity Input System + legacy keyboard fallback
- Main Scene: `Assets/Scenes/SampleScene.unity`
- Main gameplay script: `Assets/PlayerBounds2D.cs`

## Features

- Player movement with camera/manual boundary clamping
- Continuous shooting with cooldown
- Bullet spawning from player/fire point
- Bullet side-part layout adjustment (left/right visual alignment)
- Automatic projectile movement and auto-destroy lifetime
- Enemy spawning from scene/prefab candidates
- Enemy downward movement and off-screen cleanup
- Bullet hit detection with trigger colliders
- Enemy HP system and kill handling
- Enemy hit overlay effect support (template-based)
- Scene enemy/template auto-preparation logic for easier startup

## Unity Version and Dependencies

- Unity Editor: `6000.4.3f1`
- Notable packages (from `Packages/manifest.json`):
  - `com.unity.inputsystem` 1.19.0
  - `com.unity.render-pipelines.universal` 17.4.0
  - `com.unity.2d.animation` 14.0.3
  - `com.unity.2d.tilemap.extras` 7.0.1

## How To Run

1. Open Unity Hub.
2. Add this folder as a project.
3. Open with Unity Editor `6000.4.3f1` (recommended).
4. Open scene: `Assets/Scenes/SampleScene.unity`.
5. Press Play.

## Controls

- Move: `WASD` or arrow keys
- Shoot: `Space`

The movement and shooting code supports both:

- New Input System (`UnityEngine.InputSystem`)
- Legacy input fallback (`Input.GetAxisRaw`, `Input.GetKey`)

## Build Settings

The build scene list currently includes:

- `Assets/Scenes/SampleScene.unity`

## Code Architecture

Current gameplay logic is centralized in one script file:

- `Assets/PlayerBounds2D.cs`

Classes in this file:

1. `PlayerBounds2D`
   - Handles player movement, boundary clamping, shooting, bullet source resolution
   - Auto-creates an enemy spawner if one is not found in scene

2. `ProjectileMover2D`
   - Moves bullet in configured direction
   - Destroys bullet after lifetime

3. `PlayerBulletHit2D`
   - Ensures trigger colliders and rigidbody setup for bullets
   - Adds trigger colliders to child sprite parts if needed

4. `EnemyHurtboxRelay2D`
   - Relays trigger hits from child colliders to parent enemy unit

5. `EnemyUnit2D`
   - Enemy movement, health, hit reaction, death behavior
   - Overlay hit effect control and cleanup

6. `TimedDestroy2D`
   - Generic timed self-destroy helper

7. `EnemySpawner2D`
   - Picks enemy templates/sources
   - Spawns enemies from top of camera bounds at randomized intervals
   - Handles scene template cleanup and scene-enemy attachment logic

## Gameplay Flow

1. Scene starts.
2. Player component checks for spawner and creates one if absent.
3. Player reads movement input each frame.
4. Player fires bullets while shoot key is held and cooldown allows.
5. Spawner periodically creates enemies near top camera range.
6. Enemies move downward.
7. Bullet trigger hit reduces enemy HP.
8. Enemy dies when HP reaches 0.

## Notes and Limitations

- Many gameplay classes are currently in a single file (`PlayerBounds2D.cs`).
- Naming-based discovery is used for some template objects (`Enemy A/B/C`, `... hit`).
- Scene/template object naming consistency is important for automatic setup behavior.
- This is a prototype-style structure and can be refactored into separate scripts and prefab pipelines.

## Suggested Next Improvements

- Split each gameplay class into separate script files
- Add score system and UI
- Add player health and game over flow
- Add pooling for bullets/enemies
- Add sound effects and VFX polish
- Add automated play mode tests for core loops

## Repository Purpose

This repository is intended as a working baseline for a Unity 2D vertical shooter practice project. It can be used to learn core shooter loop implementation and Unity scene/component wiring.
