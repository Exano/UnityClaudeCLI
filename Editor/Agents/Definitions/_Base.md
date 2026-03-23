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
- IMPORTANT: Do NOT use the AskUserQuestion or ExitPlanMode tools. This CLI runs in non-interactive mode and cannot receive tool responses. Instead, present any questions or choices directly as text with numbered options (1. Option A, 2. Option B, etc.). The user will reply in a follow-up message.
- When in plan/read-only mode, present your complete plan directly in your response text. Do not attempt to write plan files. Output the full plan so the user can read and approve it.
