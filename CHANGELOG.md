# Changelog

## [0.1.10] - 2026-08-19

### Added
- Claude Fable 5 in the model dropdown

### Changed
- Sonnet and Opus entries now point at `claude-sonnet-5` / `claude-opus-5` (were pinned to the 4-6 generation)

### Fixed
- Clamp the serialized model index so a selection made on a newer package version cannot throw when the choice list is shorter
- Assets are refreshed without waiting for the Editor to regain OS focus. Unity only scans for externally-changed files on focus, so scripts written while Unity sat in the background were never compiled until the user alt-tabbed
- Agent definitions now state that rule and carry the `refresh_unity` procedure, so the model stops escalating to asset reimports and play mode when a compile appears not to happen

## [0.1.3] - 2026-03-10

### Fixed
- Removed DisallowAutoRefresh — was blocking MCP server refresh calls during active requests
- Domain reload prevention now uses only LockReloadAssemblies (allows asset imports, blocks C# reload)
- Fixed orphan Unlock calls after domain reload that broke the reload gate
- Auto-continue after domain reload includes original request context so Claude resumes work
- Deferred AssetDatabase.Refresh to prevent immediate reload before UI state is saved

### Improved
- Thinking display: live list of entries while streaming, collapses to foldout when done
- Input text and attachments now survive domain reloads
- Attachment struct marked Serializable for persistence

## [0.1.0] - 2026-03-06

### Added
- Initial release
- Dockable Unity Editor window (Window > Claude Code, Ctrl+Shift+K)
- Real-time streaming output from Claude Code CLI
- Markdown rendering (headers, code blocks, lists, tables, blockquotes)
- Tool use visibility with collapsible foldout
- Thinking display foldout
- Action button detection (numbered choices, yes/no questions)
- Conversation continuity via session ID
- Domain reload protection (DisallowAutoRefresh during active requests)
- Process exit safety valve
- Catppuccin Mocha dark theme
