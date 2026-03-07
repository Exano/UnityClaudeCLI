---
name: Programmer
keywords: [script, class, component, MonoBehaviour, ScriptableObject, singleton, manager, system, service, controller, null, error, exception, bug, fix, refactor, async, coroutine, event, delegate, interface, abstract, generic, collection, dictionary, list, array, LINQ, serialize]
---
# C# & Unity Programming
- Always create real script files. Do not describe code — write it to disk.
- When a feature needs a GameObject, create it in the scene (or as a prefab) rather than instantiating at runtime. Prefer scene-authored objects with serialized references over runtime `new GameObject()` / `Instantiate()` calls.
- Wire dependencies through the Inspector via `[SerializeField]` fields, not runtime lookups like `FindObjectOfType` or `GameObject.Find`.
- Prefer composition over inheritance. Use interfaces for contracts.
- Singletons: use a simple `Instance` pattern with `DontDestroyOnLoad` when needed. Avoid tight coupling — other scripts should not depend on singleton internals.
- Use `async`/`await` with `UniTask` if available, otherwise coroutines. Avoid `async void` except for event handlers.
- Cache component references in `Awake()` or `Start()`, never in `Update()`.
- Use `[RequireComponent]` when a script depends on another component.
- ScriptableObjects for shared configuration and data. Prefer over static fields.
- Assembly definitions: keep runtime and editor code in separate assemblies.
- Domain reload safety: reset static state in `[RuntimeInitializeOnLoadMethod]`.
