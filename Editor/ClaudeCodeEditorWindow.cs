using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClaudeCode.Editor.Agents;
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
        [SerializeField] private PermissionMode _permissionMode = PermissionMode.AutoApprove;
        [SerializeField] private bool _lastRunWasPlanMode;
        [SerializeField] private bool _continueConversation = true;
        [SerializeField] private string _lastSessionId;
        [SerializeField] private bool _wasRunning;
        [SerializeField] private int _modelIndex;     // 0 = Sonnet, 1 = Opus
        [SerializeField] private int _maxTurns;        // 0 = unlimited
        [SerializeField] private string _pendingInputText;               // survives domain reload
        [SerializeField] private List<Attachment> _savedAttachments = new List<Attachment>();
        [SerializeField] private string _currentConversationId;
        [SerializeField] private string _conversationCreatedAt;

        private static Texture2D s_tabIcon;
        private static readonly string[] k_ModelChoices = { "Sonnet", "Opus" };
        private static readonly string[] k_ModelIds = { "claude-sonnet-4-6", "claude-opus-4-6" };

        // UI elements (rebuilt each CreateGUI)
        private ScrollView _outputScroll;
        private TextField _inputField;
        private Button _sendButton;
        private Button _cancelButton;
        private Label _statusLabel;
        private Label _usageLabel;
        private PopupField<string> _permissionModeDropdown;
        private Toggle _continueToggle;
        private PopupField<string> _modelDropdown;
        private SliderInt _maxTurnsSlider;
        private Label _maxTurnsLabel;
        private Label _activityIndicator;
        private IVisualElementScheduledItem _activityAnim;

        // Attachments
        private List<Attachment> _attachments = new List<Attachment>();
        private VisualElement _attachmentChips;

        // Agents
        private List<AgentDefinition> _selectedAgents = new List<AgentDefinition>();
        private HashSet<string> _pinnedAgents = new HashSet<string>(); // manually toggled
        private VisualElement _agentChips;
        private string _lastAutoDetectInput;

        // Transient state (reset each domain)
        private ClaudeProcess _process;
        private MessageGroup _currentGroup;
        private float _processExitedAt;
        private bool _pendingAutoContinue;
        private bool _reloadLocked; // tracks whether we hold the lock in THIS AppDomain
        private VisualElement _historyPanel;
        private bool _historyPanelVisible;
        private VisualElement _settingsPanel;
        private bool _settingsPanelVisible;
        private TextField _mcpUrlField;

        [MenuItem("Window/Claude Code %#k")]
        public static void ShowWindow()
        {
            var window = GetWindow<ClaudeCodeEditorWindow>();
            window.titleContent = new GUIContent("Claude Code", GenerateTabIcon());
            window.minSize = new Vector2(450, 300);
        }

        /// <summary>
        /// Public API for external packages to attach a file and open/focus the window.
        /// Called via reflection by packages with an optional dependency on this package.
        /// </summary>
        public static void AttachFileAndFocus(string path, string displayName, string typeLabel)
        {
            var window = GetWindow<ClaudeCodeEditorWindow>();
            window.titleContent = new GUIContent("Claude Code", GenerateTabIcon());
            window.minSize = new Vector2(450, 300);
            window.OnAttachmentAdded(new Attachment
            {
                DisplayName = displayName,
                Path = path,
                TypeLabel = typeLabel,
                IsSceneObject = false
            });
            window.Show();
            window.Focus();
        }

        /// <summary>16x16 terminal-prompt icon in Unity blue. Cached in static field.</summary>
        private static Texture2D GenerateTabIcon()
        {
            if (s_tabIcon != null) return s_tabIcon;
            // 16x16, each char: . = transparent, # = accent blue, o = dim gray
            var rows = new[]
            {
                "................",
                "................",
                "..##............",
                "...##...........",
                "....##..........",
                ".....##.........",
                "......##........",
                ".......##.......",
                "......##........",
                ".....##.........",
                "....##..........",
                "...##...........",
                "..##............",
                ".........oooooo.",
                "................",
                "................",
            };

            var purple = new Color(76/255f, 126/255f, 255/255f, 1f);  // Unity blue
            var dim    = new Color(112/255f, 112/255f, 112/255f, 1f); // Neutral gray
            var clear  = new Color(0, 0, 0, 0);

            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };

            for (int y = 0; y < 16; y++)
            {
                var row = rows[15 - y]; // Texture2D is bottom-up
                for (int x = 0; x < 16; x++)
                {
                    var c = x < row.Length ? row[x] : '.';
                    tex.SetPixel(x, y, c == '#' ? purple : c == 'o' ? dim : clear);
                }
            }
            tex.Apply();
            s_tabIcon = tex;
            return s_tabIcon;
        }

        private void OnEnable()
        {
            AgentDiscovery.Refresh();
            _process = new ClaudeProcess(Path.GetDirectoryName(Application.dataPath));
            if (!string.IsNullOrEmpty(_lastSessionId))
                _process.LastSessionId = _lastSessionId;
            EditorApplication.update += PollProcessOutput;

            // Auto-register MCP server if Claude CLI is available
            if (_process.IsClaudeAvailable())
            {
                var mcpStatus = _process.EnsureMcpRegistered();
                if (mcpStatus != null)
                    _messageHistory.Add(new ChatMessage
                    {
                        role = ChatMessage.Role.System,
                        text = mcpStatus
                    });
            }

            // Recover from domain reload that killed a running process
            if (_wasRunning)
            {
                _wasRunning = false;
                // Lock state does not survive domain reload — no Unlock needed here.
                _continueConversation = true;
                _pendingAutoContinue = true;
                _messageHistory.Add(new ChatMessage
                {
                    role = ChatMessage.Role.System,
                    text = "[Domain reload interrupted the running task. Automatically continuing\u2026]"
                });
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollProcessOutput;
            if (_reloadLocked)
            {
                _reloadLocked = false;
                EditorApplication.UnlockReloadAssemblies();
            }
            // Preserve input text and attachments across domain reload
            if (_inputField != null)
                _pendingInputText = _inputField.value;
            _savedAttachments = new List<Attachment>(_attachments);
            SaveCurrentConversation();
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
            var settingsBtn = new Button(ToggleSettingsPanel) { text = "\u2699" };
            settingsBtn.AddToClassList("toolbar-btn");
            toolbar.Add(settingsBtn);
            var historyBtn = new Button(ToggleHistoryPanel) { text = "History \u25be" };
            historyBtn.AddToClassList("toolbar-btn");
            toolbar.Add(historyBtn);
            var newBtn = new Button(NewConversation) { text = "New" };
            newBtn.AddToClassList("toolbar-btn");
            toolbar.Add(newBtn);
            var importBtn = new Button(() => FileImportWindow.Show(OnAttachmentAdded)) { text = "Import" };
            importBtn.AddToClassList("toolbar-btn");
            toolbar.Add(importBtn);
            root.Add(toolbar);

            // Settings panel (hidden by default)
            _settingsPanel = new VisualElement();
            _settingsPanel.AddToClassList("settings-panel");
            _settingsPanel.style.display = DisplayStyle.None;
            var mcpRow = new VisualElement();
            mcpRow.AddToClassList("settings-row");
            var mcpLabel = new Label("MCP Server");
            mcpLabel.AddToClassList("settings-label");
            mcpRow.Add(mcpLabel);
            _mcpUrlField = new TextField();
            _mcpUrlField.AddToClassList("settings-field");
            _mcpUrlField.value = ClaudeProcess.GetMcpBaseUrl();
            mcpRow.Add(_mcpUrlField);
            var applyBtn = new Button(ApplyMcpUrl) { text = "Apply" };
            applyBtn.AddToClassList("settings-apply-btn");
            mcpRow.Add(applyBtn);
            _settingsPanel.Add(mcpRow);
            root.Add(_settingsPanel);

            // History panel (hidden by default)
            _historyPanel = new VisualElement();
            _historyPanel.AddToClassList("history-panel");
            _historyPanel.style.display = DisplayStyle.None;
            root.Add(_historyPanel);

            // Output scroll
            _outputScroll = new ScrollView(ScrollViewMode.Vertical);
            _outputScroll.AddToClassList("output-scroll");
            root.Add(_outputScroll);

            // Input area
            var inputContainer = new VisualElement();
            inputContainer.AddToClassList("input-container");

            // Activity indicator (animated, hidden until running)
            _activityIndicator = new Label();
            _activityIndicator.AddToClassList("activity-indicator");
            _activityIndicator.style.display = DisplayStyle.None;
            inputContainer.Add(_activityIndicator);

            var inputRow = new VisualElement();
            inputRow.AddToClassList("input-row");
            _inputField = new TextField();
            _inputField.multiline = true;
            _inputField.AddToClassList("input-field");
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            _inputField.RegisterValueChangedCallback(OnInputChanged);
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
            var permChoices = new List<string> { "Default", "Auto-approve", "Plan" };
            _permissionModeDropdown = new PopupField<string>(permChoices, (int)_permissionMode);
            _permissionModeDropdown.AddToClassList("model-dropdown");
            _permissionModeDropdown.RegisterValueChangedCallback(e =>
                _permissionMode = (PermissionMode)permChoices.IndexOf(e.newValue));
            optionsRow.Add(_permissionModeDropdown);

            // Model selector
            _modelDropdown = new PopupField<string>(
                new List<string>(k_ModelChoices), _modelIndex);
            _modelDropdown.AddToClassList("model-dropdown");
            _modelDropdown.RegisterValueChangedCallback(e =>
                _modelIndex = Array.IndexOf(k_ModelChoices, e.newValue));
            optionsRow.Add(_modelDropdown);

            // Max turns (grouped as one pill)
            var maxTurnsGroup = new VisualElement();
            maxTurnsGroup.AddToClassList("max-turns-group");
            _maxTurnsLabel = new Label(_maxTurns == 0 ? "Turns: \u221e" : $"Turns: {_maxTurns}");
            _maxTurnsLabel.AddToClassList("max-turns-label");
            maxTurnsGroup.Add(_maxTurnsLabel);
            _maxTurnsSlider = new SliderInt(0, 25) { value = _maxTurns };
            _maxTurnsSlider.AddToClassList("max-turns-slider");
            _maxTurnsSlider.RegisterValueChangedCallback(e =>
            {
                _maxTurns = e.newValue;
                _maxTurnsLabel.text = _maxTurns == 0 ? "Turns: \u221e" : $"Turns: {_maxTurns}";
            });
            maxTurnsGroup.Add(_maxTurnsSlider);
            optionsRow.Add(maxTurnsGroup);

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

            // Attachment chips row (between input and options)
            _attachmentChips = new VisualElement();
            _attachmentChips.AddToClassList("attachment-chips");
            _attachmentChips.style.display = DisplayStyle.None;
            inputContainer.Insert(inputContainer.IndexOf(optionsRow), _attachmentChips);

            // Agent chips row (between attachments and options)
            _agentChips = new VisualElement();
            _agentChips.AddToClassList("agent-chips");
            _agentChips.style.display = DisplayStyle.None;
            inputContainer.Insert(inputContainer.IndexOf(optionsRow), _agentChips);

            root.Add(inputContainer);

            // Drag-and-drop: register on the whole input area
            DragDropHandler.Register(inputContainer, _statusLabel, OnAttachmentAdded);

            RebuildFromHistory();
            RebuildAgentChips();

            // Restore input text and attachments from before domain reload
            if (!string.IsNullOrEmpty(_pendingInputText))
            {
                _inputField.SetValueWithoutNotify(_pendingInputText);
                _pendingInputText = null;
            }
            if (_savedAttachments != null && _savedAttachments.Count > 0)
            {
                _attachments = new List<Attachment>(_savedAttachments);
                _savedAttachments.Clear();
                RebuildAttachmentChips();
            }

            _inputField.schedule.Execute(() => _inputField.Focus());

            // Auto-continue after domain reload once UI is ready
            if (_pendingAutoContinue)
            {
                _pendingAutoContinue = false;
                // Find the last user message to give Claude context about what it was doing
                string lastUserMsg = null;
                for (int i = _messageHistory.Count - 1; i >= 0; i--)
                {
                    if (_messageHistory[i].role == ChatMessage.Role.User)
                    {
                        lastUserMsg = _messageHistory[i].text;
                        break;
                    }
                }
                var continuePrompt = "A domain reload (recompilation) just occurred in Unity, which killed your previous process. "
                    + "Continue exactly where you left off \u2014 do NOT ask what to work on.";
                if (!string.IsNullOrEmpty(lastUserMsg))
                    continuePrompt += $"\n\nThe original request was:\n{lastUserMsg}";
                _inputField.schedule.Execute(() => SendMessageFromAction(continuePrompt));
            }
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

            // Create conversation on first message
            if (string.IsNullOrEmpty(_currentConversationId))
            {
                _currentConversationId = Guid.NewGuid().ToString("N");
                _conversationCreatedAt = DateTime.Now.ToString("o");
            }

            _inputField.value = "";
            _pendingInputText = null;
            _usageLabel.style.display = DisplayStyle.None;

            // Show the user's typed text in chat (without attachment/agent noise)
            var displayText = text;
            if (_attachments.Count > 0)
                displayText += $"  [+{_attachments.Count} attached]";
            if (_selectedAgents.Count > 0)
            {
                var names = new List<string>();
                foreach (var a in _selectedAgents) names.Add(a.Name);
                displayText += $"  [{string.Join(", ", names)}]";
            }

            // Build the actual prompt: agent context + user text + attachments
            var prompt = BuildPromptWithAgentContext(BuildPromptWithAttachments(text));

            Record(ChatMessage.Role.User, displayText);
            AddUserBlock(displayText);
            _lastRunWasPlanMode = _permissionMode == PermissionMode.Plan;
            BeginStreamingResponse();
            _process.SendMessage(prompt, _continueConversation, _permissionMode,
                k_ModelIds[_modelIndex], _maxTurns);
            SetRunning(true);
            _inputField.Focus();
        }

        private void OnCancelClicked() => _process?.Cancel();

        private void SendMessageFromAction(string text)
        {
            if (_process == null || _process.CurrentState == ClaudeProcess.ProcessState.Running) return;
            _usageLabel.style.display = DisplayStyle.None;
            var prompt = BuildPromptWithAgentContext(text);
            Record(ChatMessage.Role.User, text);
            AddUserBlock(text);
            BeginStreamingResponse();
            _lastRunWasPlanMode = _permissionMode == PermissionMode.Plan;
            _process.SendMessage(prompt, true, _permissionMode,
                k_ModelIds[_modelIndex], _maxTurns); // always continue for action buttons
            SetRunning(true);
        }

        private void ClearAll()
        {
            if (_reloadLocked)
            {
                _reloadLocked = false;
                EditorApplication.UnlockReloadAssemblies();
            }
            _wasRunning = false;
            _outputScroll.Clear();
            _messageHistory.Clear();
            _currentGroup = null;
            _lastSessionId = null;
            _currentConversationId = null;
            _conversationCreatedAt = null;
            if (_process != null) _process.LastSessionId = null;
            _usageLabel.text = "";
            _usageLabel.style.display = DisplayStyle.None;
            _attachments.Clear();
            _savedAttachments.Clear();
            _pendingInputText = null;
            RebuildAttachmentChips();
            _selectedAgents.Clear();
            _pinnedAgents.Clear();
            _lastAutoDetectInput = null;
            RebuildAgentChips();
        }

        // ── Conversation history ──

        private void NewConversation()
        {
            SaveCurrentConversation();
            ClearAll();
        }

        private void SaveCurrentConversation()
        {
            if (string.IsNullOrEmpty(_currentConversationId)) return;

            // Only save if there's at least one user message
            bool hasUser = false;
            foreach (var msg in _messageHistory)
                if (msg.role == ChatMessage.Role.User) { hasUser = true; break; }
            if (!hasUser) return;

            ConversationStore.Save(new ConversationData
            {
                id = _currentConversationId,
                sessionId = _lastSessionId,
                createdAt = _conversationCreatedAt,
                messages = new List<ChatMessage>(_messageHistory)
            });
        }

        private void LoadConversation(string id)
        {
            var data = ConversationStore.Load(id);
            if (data == null) return;

            SaveCurrentConversation();

            // Clear UI
            _outputScroll.Clear();
            _currentGroup = null;
            _usageLabel.text = "";
            _usageLabel.style.display = DisplayStyle.None;
            _attachments.Clear();
            _savedAttachments.Clear();
            RebuildAttachmentChips();

            // Load conversation data
            _currentConversationId = data.id;
            _conversationCreatedAt = data.createdAt;
            _lastSessionId = data.sessionId;
            _messageHistory = new List<ChatMessage>(data.messages);
            if (_process != null) _process.LastSessionId = data.sessionId;
            _continueConversation = true;
            if (_continueToggle != null) _continueToggle.value = true;

            RebuildFromHistory();

            // Close history panel
            _historyPanelVisible = false;
            _historyPanel.style.display = DisplayStyle.None;
            _inputField?.Focus();
        }

        private void ToggleHistoryPanel()
        {
            _historyPanelVisible = !_historyPanelVisible;
            _historyPanel.style.display = _historyPanelVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_historyPanelVisible)
            {
                SaveCurrentConversation();
                RefreshHistoryPanel();
            }
        }

        private void RefreshHistoryPanel()
        {
            _historyPanel.Clear();
            var conversations = ConversationStore.ListAll();

            if (conversations.Count == 0)
            {
                var empty = new Label("No saved conversations");
                empty.AddToClassList("history-empty");
                _historyPanel.Add(empty);
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("history-scroll");

            foreach (var conv in conversations)
            {
                var convId = conv.id;
                var item = new VisualElement();
                item.AddToClassList("history-item");
                if (convId == _currentConversationId)
                    item.AddToClassList("history-item--active");

                var textArea = new VisualElement();
                textArea.AddToClassList("history-item-text");
                textArea.AddManipulator(new Clickable(() => LoadConversation(convId)));

                var titleLabel = new Label(conv.title ?? "Untitled");
                titleLabel.AddToClassList("history-item-title");
                textArea.Add(titleLabel);

                var dateStr = "";
                if (DateTime.TryParse(conv.updatedAt, out var dt))
                    dateStr = dt.ToString("MMM d, h:mm tt");
                var msgCount = conv.messages?.Count ?? 0;
                var metaLabel = new Label($"{dateStr}  \u00b7  {msgCount} msgs");
                metaLabel.AddToClassList("history-item-meta");
                textArea.Add(metaLabel);

                item.Add(textArea);

                var deleteBtn = new Button(() =>
                {
                    ConversationStore.Delete(convId);
                    if (_currentConversationId == convId)
                    {
                        _currentConversationId = null;
                        _conversationCreatedAt = null;
                    }
                    RefreshHistoryPanel();
                }) { text = "\u00d7" };
                deleteBtn.AddToClassList("history-item-delete");
                item.Add(deleteBtn);

                scroll.Add(item);
            }

            _historyPanel.Add(scroll);
        }

        // ── Settings ──

        private void ToggleSettingsPanel()
        {
            _settingsPanelVisible = !_settingsPanelVisible;
            _settingsPanel.style.display = _settingsPanelVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_settingsPanelVisible)
                _mcpUrlField.SetValueWithoutNotify(ClaudeProcess.GetMcpBaseUrl());
        }

        private void ApplyMcpUrl()
        {
            var url = _mcpUrlField?.value?.Trim();
            if (string.IsNullOrEmpty(url)) return;

            // Store the base URL (GetMcpUrl will append /mcp)
            url = url.TrimEnd('/');
            if (url.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - 4);

            UnityEditor.EditorPrefs.SetString("ClaudeCode.McpUrl", url);
            _mcpUrlField.SetValueWithoutNotify(url);

            var fullUrl = ClaudeProcess.GetMcpUrl();
            var status = _process?.EnsureMcpRegistered(fullUrl);
            if (!string.IsNullOrEmpty(status))
            {
                Record(ChatMessage.Role.System, status);
                AddSystemBlock(status);
                ScrollToBottom();
            }
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
            _currentGroup = new MessageGroup(_lastRunWasPlanMode);
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

        // ── Attachments ──

        private void OnAttachmentAdded(Attachment attachment)
        {
            // Deduplicate by path
            foreach (var a in _attachments)
                if (a.Path == attachment.Path && a.DisplayName == attachment.DisplayName) return;

            _attachments.Add(attachment);
            RebuildAttachmentChips();
            _inputField.Focus();
        }

        private void RemoveAttachment(int index)
        {
            if (index >= 0 && index < _attachments.Count)
            {
                _attachments.RemoveAt(index);
                RebuildAttachmentChips();
            }
        }

        private void RebuildAttachmentChips()
        {
            _attachmentChips.Clear();
            if (_attachments.Count == 0)
            {
                _attachmentChips.style.display = DisplayStyle.None;
                return;
            }

            _attachmentChips.style.display = DisplayStyle.Flex;
            for (int i = 0; i < _attachments.Count; i++)
            {
                var idx = i; // capture for closure
                var a = _attachments[i];

                var chip = new VisualElement();
                chip.AddToClassList("attachment-chip");

                var label = new Label($"{a.TypeLabel}: {a.DisplayName}");
                label.AddToClassList("attachment-chip-label");
                chip.Add(label);

                var removeBtn = new Button(() => RemoveAttachment(idx)) { text = "\u00d7" };
                removeBtn.AddToClassList("attachment-chip-remove");
                chip.Add(removeBtn);

                _attachmentChips.Add(chip);
            }
        }

        private string BuildPromptWithAttachments(string userText)
        {
            if (_attachments.Count == 0) return userText;

            var sb = new StringBuilder();
            sb.Append(userText);
            sb.Append("\n\nAttached references (use Read/Grep to inspect):");
            for (int i = 0; i < _attachments.Count; i++)
                sb.Append($"\n[+{i + 1}] {_attachments[i].ToPromptReference()}");

            _attachments.Clear();
            RebuildAttachmentChips();
            return sb.ToString();
        }

        // ── Agents ──

        private void OnInputChanged(ChangeEvent<string> evt)
        {
            _pendingInputText = evt.newValue;
            var text = evt.newValue?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                // Clear auto-detected agents but keep pinned ones
                _selectedAgents.RemoveAll(a => !_pinnedAgents.Contains(a.Name));
                _lastAutoDetectInput = null;
                RebuildAgentChips();
                return;
            }

            // Only re-detect when input has changed significantly
            if (_lastAutoDetectInput != null &&
                Math.Abs(text.Length - _lastAutoDetectInput.Length) < 8)
                return;

            _lastAutoDetectInput = text;
            var detected = AgentDiscovery.AutoDetect(text);

            // Remove old auto-detected agents (keep pinned), then add new detections
            _selectedAgents.RemoveAll(a => !_pinnedAgents.Contains(a.Name));
            foreach (var agent in detected)
            {
                bool alreadySelected = false;
                foreach (var s in _selectedAgents)
                {
                    if (s.Name == agent.Name) { alreadySelected = true; break; }
                }
                if (!alreadySelected)
                    _selectedAgents.Add(agent);
            }
            RebuildAgentChips();
        }

        private void ToggleAgentByName(string name)
        {
            for (int i = 0; i < _selectedAgents.Count; i++)
            {
                if (_selectedAgents[i].Name == name)
                {
                    _selectedAgents.RemoveAt(i);
                    _pinnedAgents.Remove(name);
                    RebuildAgentChips();
                    return;
                }
            }
            // Not found — manually add (pin) it
            foreach (var a in AgentDiscovery.GetAgents())
            {
                if (a.Name == name)
                {
                    _selectedAgents.Add(a);
                    _pinnedAgents.Add(name);
                    RebuildAgentChips();
                    return;
                }
            }
        }

        private void RebuildAgentChips()
        {
            _agentChips.Clear();
            var allAgents = AgentDiscovery.GetAgents();

            if (allAgents.Count == 0 && _selectedAgents.Count == 0)
            {
                _agentChips.style.display = DisplayStyle.None;
                return;
            }

            _agentChips.style.display = DisplayStyle.Flex;

            // Show selected agents as active chips
            foreach (var a in _selectedAgents)
            {
                var name = a.Name;
                var chip = new VisualElement();
                chip.AddToClassList("agent-chip");
                chip.AddToClassList("agent-chip--active");

                var label = new Label(name);
                label.AddToClassList("agent-chip-label");
                chip.Add(label);

                var removeBtn = new Button(() => ToggleAgentByName(name)) { text = "\u00d7" };
                removeBtn.AddToClassList("agent-chip-remove");
                chip.Add(removeBtn);

                _agentChips.Add(chip);
            }

            // Show unselected agents as inactive chips (add button)
            foreach (var a in allAgents)
            {
                bool selected = false;
                foreach (var s in _selectedAgents)
                {
                    if (s.Name == a.Name) { selected = true; break; }
                }
                if (selected) continue;

                var name = a.Name;
                var chip = new Button(() => ToggleAgentByName(name));
                chip.AddToClassList("agent-chip");
                chip.AddToClassList("agent-chip--inactive");
                chip.text = $"+ {name}";

                _agentChips.Add(chip);
            }
        }

        private string BuildPromptWithAgentContext(string prompt)
        {
            var context = AgentDiscovery.BuildContext(_selectedAgents);
            if (string.IsNullOrEmpty(context))
                return prompt;

            return $"{context}\n\n---\n\n{prompt}";
        }

        private void Record(ChatMessage.Role role, string text)
        {
            _messageHistory.Add(new ChatMessage { role = role, text = text });
        }

        private void SetRunning(bool running)
        {
            _sendButton.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            _cancelButton.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            _statusLabel.text = running
                ? (_lastRunWasPlanMode ? "Planning\u2026" : "Working\u2026")
                : "Ready";
            _permissionModeDropdown?.SetEnabled(!running);

            // Animated activity indicator above the input field
            if (running && _activityIndicator != null)
            {
                var prefix = _lastRunWasPlanMode ? "Planning" : "Thinking";
                _activityIndicator.text = prefix;
                _activityIndicator.style.display = DisplayStyle.Flex;
                _activityIndicator.EnableInClassList("activity-indicator--plan", _lastRunWasPlanMode);
                int frame = 0;
                _activityAnim = _activityIndicator.schedule.Execute(() =>
                {
                    frame = (frame + 1) % 4;
                    _activityIndicator.text = frame switch
                    {
                        0 => prefix,
                        1 => $"{prefix} .",
                        2 => $"{prefix} . .",
                        _ => $"{prefix} . . ."
                    };
                }).Every(400);
            }
            else if (!running && _activityIndicator != null)
            {
                _activityAnim?.Pause();
                _activityAnim = null;
                _activityIndicator.style.display = DisplayStyle.None;
            }

            // Prevent domain reload from killing the process mid-task.
            // LockReloadAssemblies blocks C# recompilation/reload but still allows
            // asset imports so MCP refresh calls work normally.
            if (running && !_reloadLocked)
            {
                _reloadLocked = true;
                EditorApplication.LockReloadAssemblies();
            }
            else if (!running && _reloadLocked)
            {
                _reloadLocked = false;
                EditorApplication.UnlockReloadAssemblies();
            }

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
                // Defer the asset refresh so the UI has time to finalize and
                // serialized state is written before a potential domain reload.
                EditorApplication.delayCall += () => AssetDatabase.Refresh();
                SaveCurrentConversation();
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
