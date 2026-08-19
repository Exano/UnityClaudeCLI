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

## Compiling after code changes
Unity's asset scan is triggered by the Editor regaining OS focus. When Unity is in
the background — which is the normal case while you work — a `.cs` file you write
is never noticed, never compiled, and the edit has no effect. Unity is able to
compile and domain-reload unfocused; it just needs to be told to look.

- After writing or editing any `.cs` file, call `refresh_unity` with
  `mode: force` and `compile: request`. This is the only reliable trigger.
- Then poll `editor_state` until `ready_for_tools` is true before calling any
  other Unity tool. `refresh_unity` returns as soon as the compile is requested,
  so the assemblies behind it are not loaded yet.
- Expect the domain reload to interrupt this session. That is normal — the
  conversation resumes afterward. Do not treat it as a failure and retry.
- Do NOT try to force a reload by reimporting assets, toggling play mode, or
  touching files. None of those trigger the scan, and entering play mode with
  stale assemblies fails. If a compile seems not to happen, call `refresh_unity`
  again rather than reaching for a workaround.
- Never ask the user to click on the Unity window to make a build happen.

## General
- Prefer MCP scene operations over writing editor scripts for one-off scene tasks.
- Verify scene state via MCP before making assumptions about what exists.
