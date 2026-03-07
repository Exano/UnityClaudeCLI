---
name: _Base
---
# Unity Project Rules
- Unity 6 APIs and modern C# (null checks, proper namespaces).
- Place scripts in `Assets/Scripts/` unless specified otherwise.
- Editor scripts go in `Assets/Editor/` or folders with Editor assembly definitions.
- Use `[SerializeField]` for inspector-exposed fields, never public fields.
- Use `CompareTag()` instead of `== "string"` for tag comparison.
- Use `TryGetComponent<T>()` over `GetComponent<T>()` + null check.
- Guard editor-only code with `#if UNITY_EDITOR`.
- Always create real files and make real changes. Never just explain what to do.
- "The scene" refers to the currently active open scene in the Unity Editor.
