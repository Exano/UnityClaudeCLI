using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace ClaudeCode.Editor.Rendering
{
    /// <summary>
    /// Visual container for one assistant turn: thinking → tools → markdown content → action buttons → result.
    /// </summary>
    public class MessageGroup : VisualElement
    {
        public event Action<string> SendMessage;

        private readonly VisualElement _thinkingContainer;
        private readonly Foldout _toolsFoldout;
        private readonly VisualElement _contentContainer;
        private readonly VisualElement _actionsContainer;
        private readonly Label _resultLabel;

        private Label _streamingLabel;
        private Label _activeThinkingLabel;
        private Label _thinkingIndicator;
        private IVisualElementScheduledItem _thinkingAnim;
        private readonly StringBuilder _streamingText = new StringBuilder();
        private readonly List<string> _thinkingEntries = new List<string>();
        private readonly List<string> _toolNames = new List<string>();
        private readonly string _thinkingPrefix;
        private bool _finalized;

        public MessageGroup(bool isPlanMode = false)
        {
            _thinkingPrefix = isPlanMode ? "Planning" : "Thinking";
            AddToClassList("message-group");
            AddToClassList("claude-message");
            if (isPlanMode)
                AddToClassList("message-group--plan");

            // Thinking list (visible, each entry as a separate item)
            _thinkingContainer = new VisualElement();
            _thinkingContainer.AddToClassList("thinking-container");
            _thinkingContainer.style.display = DisplayStyle.None;
            Add(_thinkingContainer);

            // Tools (collapsed, hidden until content)
            _toolsFoldout = new Foldout { text = "Tools", value = false };
            _toolsFoldout.AddToClassList("tools-foldout");
            _toolsFoldout.style.display = DisplayStyle.None;
            Add(_toolsFoldout);

            // Content area (streaming label, then rendered markdown)
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("group-content");
            Add(_contentContainer);

            // Thinking indicator (animated dots, shown until real content arrives)
            _thinkingIndicator = new Label(_thinkingPrefix);
            _thinkingIndicator.AddToClassList("thinking-indicator");
            _contentContainer.Add(_thinkingIndicator);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);

            // Action buttons (hidden until detected)
            _actionsContainer = new VisualElement();
            _actionsContainer.AddToClassList("actions-container");
            _actionsContainer.style.display = DisplayStyle.None;
            Add(_actionsContainer);

            // Result/usage line
            _resultLabel = new Label();
            _resultLabel.AddToClassList("group-result");
            _resultLabel.style.display = DisplayStyle.None;
            Add(_resultLabel);
        }

        private void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            if (_thinkingIndicator == null) return;
            int frame = 0;
            _thinkingAnim = _thinkingIndicator.schedule.Execute(() =>
            {
                if (_thinkingIndicator == null) return;
                frame = (frame + 1) % 4;
                _thinkingIndicator.text = frame switch
                {
                    0 => _thinkingPrefix,
                    1 => $"{_thinkingPrefix} .",
                    2 => $"{_thinkingPrefix} . .",
                    _ => $"{_thinkingPrefix} . . ."
                };
            }).Every(400);
        }

        private void HideThinkingIndicator()
        {
            if (_thinkingIndicator == null) return;
            _thinkingAnim?.Pause();
            _thinkingIndicator.RemoveFromHierarchy();
            _thinkingIndicator = null;
            _thinkingAnim = null;
        }

        public void AddThinking(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            HideThinkingIndicator();
            _thinkingContainer.style.display = DisplayStyle.Flex;

            // Remove the previous "active" thinking label — it becomes a finalized entry
            if (_activeThinkingLabel != null)
            {
                _activeThinkingLabel.RemoveFromClassList("thinking-active");
                _activeThinkingLabel = null;
            }

            _thinkingEntries.Add(text);

            var item = new Label(text);
            item.AddToClassList("thinking-item");
            item.AddToClassList("thinking-active");
            item.selection.isSelectable = true;
            _thinkingContainer.Add(item);
            _activeThinkingLabel = item;
        }

        /// <summary>
        /// Called on finalize — collapse old thinking entries into a foldout, keep it tidy.
        /// </summary>
        private void CollapseThinking()
        {
            if (_thinkingEntries.Count == 0) return;

            _activeThinkingLabel = null;
            _thinkingContainer.Clear();

            var foldout = new Foldout
            {
                text = $"Thinking ({_thinkingEntries.Count} steps)",
                value = false
            };
            foldout.AddToClassList("thinking-foldout");

            foreach (var entry in _thinkingEntries)
            {
                var item = new Label(entry);
                item.AddToClassList("thinking-item");
                item.selection.isSelectable = true;
                foldout.Add(item);
            }
            _thinkingContainer.Add(foldout);
        }

        public void AddToolUse(string toolName)
        {
            HideThinkingIndicator();
            _toolNames.Add(toolName);
            var item = new Label($"\u2022 {toolName}");
            item.AddToClassList("tool-item");
            _toolsFoldout.Add(item);
            _toolsFoldout.text = $"Used {_toolNames.Count} tool{(_toolNames.Count != 1 ? "s" : "")}";
            _toolsFoldout.style.display = DisplayStyle.Flex;
        }

        public void UpdateToolDetail(string detail)
        {
            // Update the last tool item with detail info
            var tc = _toolsFoldout.contentContainer;
            if (tc.childCount > 0)
            {
                var last = tc[tc.childCount - 1] as Label;
                if (last != null)
                    last.text += $" \u2192 {detail}";
            }
        }

        public void AppendText(string text)
        {
            if (_finalized) return;
            HideThinkingIndicator();
            if (_streamingLabel == null)
            {
                _streamingLabel = new Label();
                _streamingLabel.AddToClassList("md-paragraph");
                _streamingLabel.AddToClassList("streaming-text");
                _streamingLabel.selection.isSelectable = true;
                _contentContainer.Add(_streamingLabel);
            }
            if (_streamingText.Length > 0)
                _streamingText.Append("\n\n");
            _streamingText.Append(text);
            _streamingLabel.text = _streamingText.ToString();
        }

        /// <summary>
        /// Replace streaming text with rendered markdown and detect action buttons.
        /// Returns the raw text for history recording.
        /// </summary>
        public string Finalize()
        {
            if (_finalized) return _streamingText.ToString();
            _finalized = true;

            HideThinkingIndicator();

            // Collapse thinking into a foldout now that the turn is done
            CollapseThinking();

            var rawText = _streamingText.ToString();
            if (rawText.Length == 0) return rawText;

            // Replace streaming label with rendered markdown
            _contentContainer.Clear();
            var rendered = MarkdownRenderer.Render(rawText);
            _contentContainer.Add(rendered);

            // Detect action buttons from the response
            DetectActions(rawText);

            return rawText;
        }

        public void SetResult(string info)
        {
            _resultLabel.text = info;
            _resultLabel.style.display = DisplayStyle.Flex;
        }

        // ── Action button detection ──

        internal enum ActionKind { None, NumberedOptions, YesNo, WaitingForInput }

        internal struct DetectedActions
        {
            public ActionKind Kind;
            public List<string> Items; // populated for NumberedOptions
        }

        internal static DetectedActions ClassifyActions(string text)
        {
            var result = new DetectedActions { Kind = ActionKind.None };
            var lines = text.TrimEnd().Split('\n');
            if (lines.Length == 0) return result;

            // Check for numbered options at the end
            var numberedItems = new List<string>();
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var m = Regex.Match(lines[i].Trim(), @"^\d+\.\s+(.+)");
                if (m.Success)
                    numberedItems.Insert(0, m.Groups[1].Value);
                else
                    break;
            }
            if (numberedItems.Count >= 2)
            {
                result.Kind = ActionKind.NumberedOptions;
                result.Items = numberedItems;
                return result;
            }

            // Check last line for question/input patterns
            var lastLine = lines[lines.Length - 1].Trim().ToLowerInvariant();

            // Yes/no questions (specific prompts to act)
            if (lastLine.EndsWith("?") &&
                (lastLine.Contains("want me to") || lastLine.Contains("should i") ||
                 lastLine.Contains("shall i") || lastLine.Contains("ready to") ||
                 lastLine.Contains("would you like")))
            {
                result.Kind = ActionKind.YesNo;
                return result;
            }

            // Waiting for input (Claude paused to ask questions or get feedback)
            // Check last two lines — the question may be on the second-to-last line
            bool hasQuestion = false;
            for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 2; i--)
            {
                var line = lines[i].Trim().ToLowerInvariant();
                if (line.EndsWith("?") || line.Contains("questions") ||
                    line.Contains("what do you think") || line.Contains("your thoughts") ||
                    line.Contains("your preference") || line.Contains("let me know") ||
                    line.Contains("please clarify") || line.Contains("which option") ||
                    line.Contains("which approach") || line.Contains("waiting"))
                {
                    hasQuestion = true;
                    break;
                }
            }
            if (hasQuestion)
            {
                result.Kind = ActionKind.WaitingForInput;
                return result;
            }

            return result;
        }

        private void DetectActions(string text)
        {
            var detected = ClassifyActions(text);
            switch (detected.Kind)
            {
                case ActionKind.NumberedOptions:
                    foreach (var item in detected.Items)
                        AddAction(item, item);
                    break;
                case ActionKind.YesNo:
                    AddAction("Yes, go ahead", "Yes, go ahead.");
                    AddAction("Modify plan", "I'd like to modify the plan:");
                    break;
                case ActionKind.WaitingForInput:
                    AddAction("Continue", "Continue, please proceed.");
                    AddAction("I have input", "Here's my input:");
                    break;
            }
        }

        private void AddAction(string label, string message)
        {
            _actionsContainer.style.display = DisplayStyle.Flex;
            var btn = new Button(() => SendMessage?.Invoke(message)) { text = label };
            btn.AddToClassList("action-btn");
            _actionsContainer.Add(btn);
        }
    }
}
