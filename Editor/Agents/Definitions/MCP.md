---
name: MCP
keywords: [MCP, hierarchy, scene, console, inspect, gameobject, component, log, test, introspect, query, runtime, spawn, create, add, modify, move, delete, parent, transform, prefab, instantiate]
---
# MCP Unity Introspection & Scene Modification Service
- A local MCP server may be running for Unity scene introspection and modification.
- Default endpoint: configured in Claude Code settings (typically `http://127.0.0.1:8080/mcp`).
- You can **read and modify** the currently open scene through MCP tools:
  - Inspect hierarchy to find GameObjects by name/path
  - Read component values and serialized fields
  - Create, move, rename, and delete GameObjects in the scene
  - Add/remove components on GameObjects
  - Set component property values (transforms, serialized fields, etc.)
  - Instantiate prefabs into the scene
  - Parent/unparent objects in the hierarchy
  - Check console logs for errors and warnings
  - Run EditMode/PlayMode tests and read results
- To create a GameObject and attach a script: use MCP to create the GameObject in the active scene first, then use MCP to add the script component to it. This is a two-step operation — do not try to do both in a single call.
- "Add it to the scene" / "put it in the scene" means the currently active open scene in the editor.
- Prefer MCP scene operations over generating editor scripts for one-off scene setup tasks.
- Always verify the current scene state via MCP before making assumptions.
- MCP data reflects the current editor/play state — results may change between calls.
