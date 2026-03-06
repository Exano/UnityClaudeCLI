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

        private readonly Foldout _thinkingFoldout;
        private readonly Foldout _toolsFoldout;
        private readonly VisualElement _contentContainer;
        private readonly VisualElement _actionsContainer;
        private readonly Label _resultLabel;

        private Label _streamingLabel;
        private readonly StringBuilder _streamingText = new StringBuilder();
        private readonly List<string> _toolNames = new List<string>();
        private bool _finalized;

        public MessageGroup()
        {
            AddToClassList("message-group");
            AddToClassList("claude-message");

            // Thinking (collapsed, hidden until content)
            _thinkingFoldout = new Foldout { text = "Show thinking", value = false };
            _thinkingFoldout.AddToClassList("thinking-foldout");
            _thinkingFoldout.style.display = DisplayStyle.None;
            Add(_thinkingFoldout);

            // Tools (collapsed, hidden until content)
            _toolsFoldout = new Foldout { text = "Tools", value = false };
            _toolsFoldout.AddToClassList("tools-foldout");
            _toolsFoldout.style.display = DisplayStyle.None;
            Add(_toolsFoldout);

            // Content area (streaming label, then rendered markdown)
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("group-content");
            Add(_contentContainer);

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

        public void AddThinking(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _thinkingFoldout.style.display = DisplayStyle.Flex;
            _thinkingFoldout.contentContainer.Clear();
            var label = new Label(text);
            label.AddToClassList("thinking-text");
            label.selection.isSelectable = true;
            _thinkingFoldout.Add(label);
        }

        public void AddToolUse(string toolName)
        {
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

        private void DetectActions(string text)
        {
            var lines = text.TrimEnd().Split('\n');
            if (lines.Length == 0) return;

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
                foreach (var item in numberedItems)
                    AddAction(item, item);
                return;
            }

            // Check for yes/no questions
            var lastLine = lines[lines.Length - 1].Trim().ToLowerInvariant();
            if (lastLine.EndsWith("?") &&
                (lastLine.Contains("want me to") || lastLine.Contains("should i") ||
                 lastLine.Contains("shall i") || lastLine.Contains("ready to") ||
                 lastLine.Contains("would you like")))
            {
                AddAction("Yes, go ahead", "Yes, go ahead.");
                AddAction("Modify plan", "I'd like to modify the plan:");
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
