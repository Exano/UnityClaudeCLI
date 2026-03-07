---
name: UI
keywords: [button, canvas, panel, layout, USS, UXML, UIDocument, screen, HUD, menu, dialog, popup, scroll, toolkit, UI, label, text, input, slider, toggle, dropdown, style, flex, anchor, RectTransform, UGUI, TMP, TextMeshPro]
---
# UI Development
- Prefer UI Toolkit for editor and new runtime UI. Use UGUI only when specifically requested.
- Follow MVVM: separate data (Model), presentation (View/UXML+USS), and logic (ViewModel).
- Do not use one-shot UI — create reusable components as GameObjects in the scene.
- UI Toolkit: require `UIDocument` + `PanelSettings`. Use USS for styling, inline only for dynamic values.
- UI Toolkit layout: `flex-grow: 1` to fill space, `flex-shrink: 0` for fixed elements. Use `-unity-text-align`, `-unity-font-style` (not CSS equivalents).
- UGUI: `CanvasScaler` with "Scale With Screen Size" (1920x1080). Always use anchors.
- For text input: `TextField` (UI Toolkit) or `TMP_InputField` (UGUI).
- Full UI guidelines available in `Assets/Docs/UIGuidelines.md` if present.
