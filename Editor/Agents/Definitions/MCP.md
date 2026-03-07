---
name: MCP
keywords: [MCP, hierarchy, scene, console, inspect, gameobject, component, log, test, introspect, query, runtime, spawn, create, add, modify, move, delete, parent, transform, prefab, instantiate]
---
# MCP for Unity
An MCP bridge (CoplayDev unity-mcp) is configured for this project. It gives you tools to interact with the Unity Editor directly — use them.

## When to use MCP tools
- Creating, modifying, deleting, or inspecting GameObjects in the active scene.
- Adding/removing components on scene objects.
- Reading Unity console logs.
- Running EditMode/PlayMode tests.
- Any time the user says "add to the scene", "put in the scene", "create in the scene" — they mean the currently open scene.

## Scene object references
- When a user attaches a scene object (not a file), the reference includes its name and hierarchy path (e.g. `Parent/Child`, no leading slash). Use this to find it via MCP.
- Scene hierarchy paths are NOT file paths. Do not try to Read or Grep them.
- The `manage_gameobject` create action supports `componentsToAdd` — you can create a GameObject and attach scripts in one call.

## General
- Prefer MCP scene operations over writing editor scripts for one-off scene tasks.
- Verify scene state via MCP before making assumptions about what exists.
