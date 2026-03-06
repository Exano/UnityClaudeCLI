# Unity Claude Code CLI

A dockable Unity Editor window that integrates [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) directly into the Unity Editor. Send prompts, watch Claude work with real-time streaming, and let it create and edit files in your project — all without leaving Unity.

![Unity 6](https://img.shields.io/badge/Unity-6000.0+-black?logo=unity)
![License](https://img.shields.io/github/license/Exano/UnityClaudeCLI)

## Features

- **Dockable editor window** — `Window > Claude Code` (Ctrl+Shift+K)
- **Real-time streaming** — see Claude's response as it types
- **Markdown rendering** — headers, code blocks with copy button, lists, tables, blockquotes, inline formatting
- **Tool use visibility** — collapsible foldout showing which tools Claude used and on what files
- **Thinking display** — optional foldout to see Claude's reasoning
- **Action buttons** — auto-detected from Claude's response (numbered choices, yes/no questions)
- **Conversation continuity** — resume previous sessions with the Continue toggle
- **Domain reload safe** — conversation history survives Unity recompilation; auto-refresh is locked during active requests to prevent mid-task interruption
- **Catppuccin Mocha theme** — dark, easy-on-the-eyes color scheme

## Prerequisites

- **Unity 6** (6000.0+)
- **Claude Code CLI** installed and authenticated — [installation guide](https://docs.anthropic.com/en/docs/claude-code)

Verify the CLI is working:
```bash
claude --version
```

## Installation

### Option 1: Git URL (recommended)

1. In Unity, go to **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Paste:
   ```
   https://github.com/Exano/UnityClaudeCLI.git
   ```

### Option 2: Local development

Clone into your project's `Packages/` folder:
```bash
cd YourProject/Packages
git clone https://github.com/Exano/UnityClaudeCLI.git com.tonythedev.unity-claude-code-cli
```

## Usage

1. Open **Window > Claude Code** (or press `Ctrl+Shift+K`)
2. Type a prompt and press **Enter** (Shift+Enter for newlines)
3. Claude streams its response with real-time markdown rendering
4. Tool usage (file reads, edits, creates) appears in a collapsible "Used N tools" foldout
5. When Claude asks a question, click the auto-generated action buttons or type a follow-up

### Options

| Toggle | Description |
|--------|-------------|
| **Continue** | Resume the previous conversation instead of starting fresh |
| **Auto-approve** | Pass `--dangerously-skip-permissions` so Claude can freely read/write files |

### Tips

- Add a `CLAUDE.md` file to your project root with instructions about your project structure, coding conventions, and Unity version. Claude reads this automatically.
- Use **Continue** after domain reloads to pick up where Claude left off.
- The **Clear** button resets conversation history and session ID.

## Architecture

| File | Purpose |
|------|---------|
| `ClaudeProcess.cs` | Spawns `claude -p`, parses stream-json NDJSON, thread-safe output queue |
| `ClaudeCodeEditorWindow.cs` | UI Toolkit editor window, polling loop, history persistence |
| `MarkdownRenderer.cs` | Markdown block parser and UI Toolkit element renderer |
| `MessageGroup.cs` | Visual container for one assistant turn (thinking + tools + content + actions) |
| `ClaudeCodeStyles.uss` | Catppuccin Mocha USS stylesheet |

## License

See [LICENSE](LICENSE) for details.
