using System;
using System.Collections.Generic;
using System.IO;
using ClaudeCode.Editor.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ClaudeCode.Editor
{
    public class ClaudeCodeEditorWindow : EditorWindow
    {
        private const string k_StylePath = "Packages/com.tonythedev.unity-claude-code-cli/Editor/UI/ClaudeCodeStyles.uss";

        [Serializable]
        public struct ChatMessage
        {
            public enum Role { User, Claude, System, ToolUse, Result }
            public Role role;
            public string text;
        }

        // Serialized state survives domain reload
        [SerializeField] private List<ChatMessage> _messageHistory = new List<ChatMessage>();
        [SerializeField] private bool _autoApprove = true;
        [SerializeField] private bool _continueConversation;
        [SerializeField] private string _lastSessionId;
        [SerializeField] private bool _wasRunning;

        // UI elements (rebuilt each CreateGUI)
        private ScrollView _outputScroll;
        private TextField _inputField;
        private Button _sendButton;
        private Button _cancelButton;
        private Label _statusLabel;
        private Label _usageLabel;
        private Toggle _autoApproveToggle;
        private Toggle _continueToggle;

        // Transient state
        private ClaudeProcess _process;
        private MessageGroup _currentGroup;
        private float _processExitedAt;

        [MenuItem("Window/Claude Code %#k")]
        public static void ShowWindow()
        {
            var window = GetWindow<ClaudeCodeEditorWindow>();
            window.titleContent = new GUIContent("Claude Code");
            window.minSize = new Vector2(450, 300);
        }

        private void OnEnable()
        {
            _process = new ClaudeProcess(Path.GetDirectoryName(Application.dataPath));
            if (!string.IsNullOrEmpty(_lastSessionId))
                _process.LastSessionId = _lastSessionId;
            EditorApplication.update += PollProcessOutput;

            // Recover from domain reload that killed a running process
            if (_wasRunning)
            {
                _wasRunning = false;
                // Re-allow refresh since the process is gone
                AssetDatabase.AllowAutoRefresh();
                _continueConversation = true;
                _messageHistory.Add(new ChatMessage
                {
                    role = ChatMessage.Role.System,
                    text = "[Domain reload interrupted the running task. Continue is enabled \u2014 send a message to resume.]"
                });
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollProcessOutput;
            if (_wasRunning)
                AssetDatabase.AllowAutoRefresh();
            _process?.Dispose();
            _process = null;
        }

        // ── GUI construction ──

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_StylePath);
            if (ss != null) root.styleSheets.Add(ss);
            root.AddToClassList("root");

            // Toolbar
            var toolbar = new VisualElement();
            toolbar.AddToClassList("toolbar");
            var title = new Label("Claude Code");
            title.AddToClassList("toolbar-title");
            toolbar.Add(title);
            toolbar.Add(Spacer());
            var clearBtn = new Button(ClearAll) { text = "Clear" };
            clearBtn.AddToClassList("toolbar-btn");
            toolbar.Add(clearBtn);
            root.Add(toolbar);

            // Output scroll
            _outputScroll = new ScrollView(ScrollViewMode.Vertical);
            _outputScroll.AddToClassList("output-scroll");
            root.Add(_outputScroll);

            // Input area
            var inputContainer = new VisualElement();
            inputContainer.AddToClassList("input-container");

            var inputRow = new VisualElement();
            inputRow.AddToClassList("input-row");
            _inputField = new TextField();
            _inputField.multiline = true;
            _inputField.AddToClassList("input-field");
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            inputRow.Add(_inputField);

            var btnContainer = new VisualElement();
            btnContainer.AddToClassList("btn-container");
            _sendButton = new Button(OnSendClicked) { text = "Send" };
            _sendButton.AddToClassList("send-btn");
            btnContainer.Add(_sendButton);
            _cancelButton = new Button(OnCancelClicked) { text = "Stop" };
            _cancelButton.AddToClassList("cancel-btn");
            _cancelButton.style.display = DisplayStyle.None;
            btnContainer.Add(_cancelButton);
            inputRow.Add(btnContainer);
            inputContainer.Add(inputRow);

            // Options
            var optionsRow = new VisualElement();
            optionsRow.AddToClassList("options-row");
            _continueToggle = new Toggle("Continue");
            _continueToggle.AddToClassList("option-toggle");
            _continueToggle.value = _continueConversation;
            _continueToggle.RegisterValueChangedCallback(e => _continueConversation = e.newValue);
            optionsRow.Add(_continueToggle);
            _autoApproveToggle = new Toggle("Auto-approve");
            _autoApproveToggle.AddToClassList("option-toggle");
            _autoApproveToggle.value = _autoApprove;
            _autoApproveToggle.RegisterValueChangedCallback(e => _autoApprove = e.newValue);
            optionsRow.Add(_autoApproveToggle);
            inputContainer.Add(optionsRow);

            // Status row
            var statusRow = new VisualElement();
            statusRow.AddToClassList("status-row");
            _statusLabel = new Label("Ready");
            _statusLabel.AddToClassList("status-label");
            statusRow.Add(_statusLabel);
            _usageLabel = new Label("");
            _usageLabel.AddToClassList("usage-label");
            _usageLabel.style.display = DisplayStyle.None;
            statusRow.Add(_usageLabel);
            inputContainer.Add(statusRow);

            root.Add(inputContainer);

            RebuildFromHistory();
            _inputField.schedule.Execute(() => _inputField.Focus());
        }

        // ── History rebuild ──

        private void RebuildFromHistory()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            AddSystemBlock(
                "Claude Code for Unity\n" +
                "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\n" +
                "Shift+Enter for newlines. Enter to send.\n" +
                $"Working dir: {projectRoot}");

            if (_process != null && !_process.IsClaudeAvailable())
            {
                AddSystemBlock(
                    "[!] Claude CLI not found.\n" +
                    "Install: https://docs.anthropic.com/en/docs/claude-code");
            }

            // Replay saved messages — Claude messages get rendered as markdown
            foreach (var msg in _messageHistory)
            {
                switch (msg.role)
                {
                    case ChatMessage.Role.User: AddUserBlock(msg.text); break;
                    case ChatMessage.Role.Claude: AddClaudeBlockFromHistory(msg.text); break;
                    case ChatMessage.Role.System: AddSystemBlock(msg.text); break;
                    case ChatMessage.Role.ToolUse: break; // tools are part of the group, skip standalone replay
                    case ChatMessage.Role.Result: AddResultBlock(msg.text); break;
                }
            }
        }

        // ── Event handlers ──

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
            {
                evt.PreventDefault();
                evt.StopPropagation();
                // Defer to next frame — clearing the field mid-keystroke causes
                // ArgumentOutOfRangeException in Unity's internal text editor.
                _inputField.schedule.Execute(OnSendClicked);
            }
        }

        private void OnSendClicked()
        {
            if (_process == null || _process.CurrentState == ClaudeProcess.ProcessState.Running) return;
            var text = _inputField.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _inputField.value = "";
            _usageLabel.style.display = DisplayStyle.None;
            Record(ChatMessage.Role.User, text);
            AddUserBlock(text);
            BeginStreamingResponse();
            _process.SendMessage(text, _continueConversation, _autoApprove);
            SetRunning(true);
            _inputField.Focus();
        }

        private void OnCancelClicked() => _process?.Cancel();

        private void SendMessageFromAction(string text)
        {
            if (_process == null || _process.CurrentState == ClaudeProcess.ProcessState.Running) return;
            _usageLabel.style.display = DisplayStyle.None;
            Record(ChatMessage.Role.User, text);
            AddUserBlock(text);
            BeginStreamingResponse();
            _process.SendMessage(text, true, _autoApprove); // always continue for action buttons
            SetRunning(true);
        }

        private void ClearAll()
        {
            if (_wasRunning)
                AssetDatabase.AllowAutoRefresh();
            _wasRunning = false;
            _outputScroll.Clear();
            _messageHistory.Clear();
            _currentGroup = null;
            _lastSessionId = null;
            if (_process != null) _process.LastSessionId = null;
            _usageLabel.text = "";
            _usageLabel.style.display = DisplayStyle.None;
        }

        // ── Block builders ──

        private void AddUserBlock(string text)
        {
            var b = MakeBlock("user-message");
            b.Add(MakeLabel($"> {text}", "message-content", true));
            _outputScroll.Add(b);
            ScrollToBottom();
        }

        private void AddClaudeBlockFromHistory(string text)
        {
            // Render historical Claude messages as markdown
            var b = new VisualElement();
            b.AddToClassList("message-block");
            b.AddToClassList("claude-message");
            var rendered = MarkdownRenderer.Render(text);
            b.Add(rendered);
            _outputScroll.Add(b);
            ScrollToBottom();
        }

        private void AddSystemBlock(string text)
        {
            var b = MakeBlock("system-message");
            b.Add(MakeLabel(text, "message-content", true));
            _outputScroll.Add(b);
            ScrollToBottom();
        }

        private void AddResultBlock(string info)
        {
            var b = MakeBlock("result-message");
            b.Add(MakeLabel(info, "message-content", false));
            _outputScroll.Add(b);
            ScrollToBottom();
        }

        private void BeginStreamingResponse()
        {
            _currentGroup = new MessageGroup();
            _currentGroup.SendMessage += SendMessageFromAction;
            _outputScroll.Add(_currentGroup);
        }

        private static VisualElement MakeBlock(string cls)
        {
            var ve = new VisualElement();
            ve.AddToClassList("message-block");
            ve.AddToClassList(cls);
            return ve;
        }

        private static Label MakeLabel(string text, string cls, bool selectable)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            if (selectable) l.selection.isSelectable = true;
            return l;
        }

        private static VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.flexGrow = 1;
            return s;
        }

        private void ScrollToBottom()
        {
            _outputScroll?.schedule.Execute(() =>
                _outputScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        private void Record(ChatMessage.Role role, string text)
        {
            _messageHistory.Add(new ChatMessage { role = role, text = text });
        }

        private void SetRunning(bool running)
        {
            _sendButton.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            _cancelButton.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            _statusLabel.text = running ? "Claude is thinking\u2026" : "Ready";

            // Prevent domain reload from killing the process mid-task
            if (running)
                AssetDatabase.DisallowAutoRefresh();
            else
                AssetDatabase.AllowAutoRefresh();

            _wasRunning = running;
        }

        // ── Main-thread output polling ──

        private void PollProcessOutput()
        {
            if (_process == null) return;

            bool dirty = false;
            bool done = false;

            while (_process.TryDequeue(out var c))
            {
                switch (c.Type)
                {
                    case ClaudeProcess.OutputChunk.Kind.Thinking:
                        if (_currentGroup == null) BeginStreamingResponse();
                        _currentGroup.AddThinking(c.Text);
                        break;

                    case ClaudeProcess.OutputChunk.Kind.Text:
                        if (_currentGroup == null) BeginStreamingResponse();
                        _currentGroup.AppendText(c.Text);
                        dirty = true;
                        break;

                    case ClaudeProcess.OutputChunk.Kind.ToolUse:
                        if (_currentGroup == null) BeginStreamingResponse();
                        _currentGroup.AddToolUse(c.Text);
                        _statusLabel.text = $"Using {c.Text}\u2026";
                        break;

                    case ClaudeProcess.OutputChunk.Kind.Status:
                        _statusLabel.text = c.Text;
                        if (_currentGroup != null)
                            _currentGroup.UpdateToolDetail(c.Text);
                        break;

                    case ClaudeProcess.OutputChunk.Kind.Result:
                        _usageLabel.text = c.Text;
                        _usageLabel.style.display = DisplayStyle.Flex;
                        if (_currentGroup != null)
                            _currentGroup.SetResult(c.Text);
                        break;

                    case ClaudeProcess.OutputChunk.Kind.System:
                        Record(ChatMessage.Role.System, c.Text);
                        AddSystemBlock(c.Text);
                        break;

                    case ClaudeProcess.OutputChunk.Kind.Complete:
                        done = true;
                        break;
                }
            }

            if (dirty)
            {
                ScrollToBottom();
                Repaint();
            }

            if (done)
            {
                _processExitedAt = 0;
                if (_currentGroup != null)
                {
                    var fullText = _currentGroup.Finalize();
                    if (!string.IsNullOrEmpty(fullText))
                        Record(ChatMessage.Role.Claude, fullText);
                }
                _currentGroup = null;
                if (!string.IsNullOrEmpty(_process?.LastSessionId))
                    _lastSessionId = _process.LastSessionId;
                SetRunning(false);
                ScrollToBottom();
                Repaint();
                AssetDatabase.Refresh();
            }

            // Safety valve: if the OS process exited but Complete never arrived,
            // force completion after a short grace period for final output to drain.
            if (!done && _wasRunning && _process != null && _process.HasProcessExited)
            {
                if (_processExitedAt == 0)
                    _processExitedAt = (float)EditorApplication.timeSinceStartup;
                else if ((float)EditorApplication.timeSinceStartup - _processExitedAt > 3f)
                {
                    _processExitedAt = 0;
                    Enqueue_Complete();
                }
            }
        }

        private void Enqueue_Complete()
        {
            // Force the completion path on the next poll tick
            if (_currentGroup != null)
            {
                var fullText = _currentGroup.Finalize();
                if (!string.IsNullOrEmpty(fullText))
                    Record(ChatMessage.Role.Claude, fullText);
            }
            _currentGroup = null;
            if (!string.IsNullOrEmpty(_process?.LastSessionId))
                _lastSessionId = _process.LastSessionId;
            Record(ChatMessage.Role.System, "[Process exited without completion signal]");
            AddSystemBlock("[Process exited without completion signal]");
            SetRunning(false);
            ScrollToBottom();
            Repaint();
            AssetDatabase.Refresh();
        }
    }
}
