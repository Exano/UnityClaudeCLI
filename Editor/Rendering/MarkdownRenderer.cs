using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace ClaudeCode.Editor.Rendering
{
    public static class MarkdownRenderer
    {
        public static VisualElement Render(string markdown)
        {
            var container = new VisualElement();
            container.AddToClassList("md-container");
            if (string.IsNullOrEmpty(markdown)) return container;

            var blocks = ParseBlocks(markdown);
            foreach (var block in blocks)
                container.Add(RenderBlock(block));
            return container;
        }

        // ── Block model ──

        enum BlockType { Paragraph, Header, CodeBlock, UnorderedList, OrderedList, Table, Blockquote, HorizontalRule }

        struct Block
        {
            public BlockType Type;
            public string Content;
            public int Level;
            public string Language;
            public List<string> Lines;
        }

        // ── Block parser ──

        static List<Block> ParseBlocks(string markdown)
        {
            var blocks = new List<Block>();
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                // Code block
                if (trimmed.StartsWith("```"))
                {
                    var lang = trimmed.Substring(3).Trim();
                    var codeLines = new List<string>();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        codeLines.Add(lines[i]);
                        i++;
                    }
                    if (i < lines.Length) i++;
                    blocks.Add(new Block { Type = BlockType.CodeBlock, Content = string.Join("\n", codeLines), Language = lang });
                    continue;
                }

                // Header
                if (trimmed.StartsWith("#"))
                {
                    int level = 0;
                    while (level < trimmed.Length && trimmed[level] == '#') level++;
                    if (level <= 6 && level < trimmed.Length && trimmed[level] == ' ')
                    {
                        blocks.Add(new Block { Type = BlockType.Header, Content = trimmed.Substring(level + 1).Trim(), Level = level });
                        i++;
                        continue;
                    }
                }

                // Horizontal rule
                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    blocks.Add(new Block { Type = BlockType.HorizontalRule });
                    i++;
                    continue;
                }

                // Unordered list
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    var items = new List<string>();
                    while (i < lines.Length)
                    {
                        var lt = lines[i].TrimStart();
                        if (lt.StartsWith("- ") || lt.StartsWith("* "))
                        { items.Add(lt.Substring(2)); i++; }
                        else if (lines[i].StartsWith("  ") && items.Count > 0)
                        { items[items.Count - 1] += " " + lines[i].Trim(); i++; }
                        else break;
                    }
                    blocks.Add(new Block { Type = BlockType.UnorderedList, Lines = items });
                    continue;
                }

                // Ordered list
                if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
                {
                    var items = new List<string>();
                    while (i < lines.Length)
                    {
                        var lt = lines[i].TrimStart();
                        var m = Regex.Match(lt, @"^\d+\.\s(.*)");
                        if (m.Success) { items.Add(m.Groups[1].Value); i++; }
                        else if (lines[i].StartsWith("  ") && items.Count > 0)
                        { items[items.Count - 1] += " " + lines[i].Trim(); i++; }
                        else break;
                    }
                    blocks.Add(new Block { Type = BlockType.OrderedList, Lines = items });
                    continue;
                }

                // Table
                if (trimmed.StartsWith("|"))
                {
                    var tableLines = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                    { tableLines.Add(lines[i].Trim()); i++; }
                    blocks.Add(new Block { Type = BlockType.Table, Lines = tableLines });
                    continue;
                }

                // Blockquote
                if (trimmed.StartsWith("> ") || trimmed == ">")
                {
                    var quoteLines = new List<string>();
                    while (i < lines.Length)
                    {
                        var lt = lines[i].TrimStart();
                        if (lt.StartsWith("> ")) { quoteLines.Add(lt.Substring(2)); i++; }
                        else if (lt == ">") { quoteLines.Add(""); i++; }
                        else break;
                    }
                    blocks.Add(new Block { Type = BlockType.Blockquote, Content = string.Join("\n", quoteLines) });
                    continue;
                }

                // Paragraph — collect lines until a block-level element or blank line
                {
                    var paraLines = new List<string>();
                    while (i < lines.Length)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i])) break;
                        var lt = lines[i].TrimStart();
                        if (lt.StartsWith("#") || lt.StartsWith("```") ||
                            lt.StartsWith("- ") || lt.StartsWith("* ") ||
                            lt.StartsWith("|") || lt.StartsWith("> ") ||
                            Regex.IsMatch(lt, @"^\d+\.\s") ||
                            lt == "---" || lt == "***" || lt == "___")
                            break;
                        paraLines.Add(lines[i].Trim());
                        i++;
                    }
                    if (paraLines.Count > 0)
                        blocks.Add(new Block { Type = BlockType.Paragraph, Content = string.Join(" ", paraLines) });
                }
            }

            return blocks;
        }

        // ── Block renderers ──

        static VisualElement RenderBlock(Block block)
        {
            switch (block.Type)
            {
                case BlockType.Header: return RenderHeader(block);
                case BlockType.CodeBlock: return RenderCodeBlock(block);
                case BlockType.UnorderedList: return RenderList(block, ordered: false);
                case BlockType.OrderedList: return RenderList(block, ordered: true);
                case BlockType.Table: return RenderTable(block);
                case BlockType.Blockquote: return RenderBlockquote(block);
                case BlockType.HorizontalRule: return RenderHR();
                default: return RenderParagraph(block);
            }
        }

        static Label RenderHeader(Block block)
        {
            var label = new Label(InlineFormat(block.Content));
            label.enableRichText = true;
            label.AddToClassList("md-header");
            label.AddToClassList($"md-h{block.Level}");
            label.selection.isSelectable = true;
            return label;
        }

        static Label RenderParagraph(Block block)
        {
            var label = new Label(InlineFormat(block.Content));
            label.enableRichText = true;
            label.AddToClassList("md-paragraph");
            label.selection.isSelectable = true;
            return label;
        }

        static VisualElement RenderCodeBlock(Block block)
        {
            var container = new VisualElement();
            container.AddToClassList("md-code-block");

            // Header row: language label + copy button
            var header = new VisualElement();
            header.AddToClassList("md-code-header");
            if (!string.IsNullOrEmpty(block.Language))
            {
                var langLabel = new Label(block.Language);
                langLabel.AddToClassList("md-code-lang");
                header.Add(langLabel);
            }
            header.Add(new VisualElement { style = { flexGrow = 1 } });
            var copyBtn = new Button(() => GUIUtility.systemCopyBuffer = block.Content) { text = "Copy" };
            copyBtn.AddToClassList("md-code-copy");
            header.Add(copyBtn);
            container.Add(header);

            var code = new Label(block.Content);
            code.enableRichText = false;
            code.AddToClassList("md-code-text");
            code.style.whiteSpace = WhiteSpace.PreWrap;
            code.selection.isSelectable = true;
            container.Add(code);

            return container;
        }

        static VisualElement RenderList(Block block, bool ordered)
        {
            var container = new VisualElement();
            container.AddToClassList("md-list");
            for (int i = 0; i < block.Lines.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("md-list-item");
                var bullet = new Label(ordered ? $"{i + 1}." : "\u2022");
                bullet.AddToClassList("md-bullet");
                row.Add(bullet);
                var text = new Label(InlineFormat(block.Lines[i]));
                text.enableRichText = true;
                text.AddToClassList("md-list-text");
                text.selection.isSelectable = true;
                row.Add(text);
                container.Add(row);
            }
            return container;
        }

        static VisualElement RenderTable(Block block)
        {
            var container = new VisualElement();
            container.AddToClassList("md-table");
            bool isHeader = true;

            foreach (var line in block.Lines)
            {
                if (Regex.IsMatch(line, @"^\|[\s\-:|]+\|$")) { isHeader = false; continue; }

                var row = new VisualElement();
                row.AddToClassList("md-table-row");
                if (isHeader) row.AddToClassList("md-table-header");

                var cells = line.Split('|');
                for (int c = 0; c < cells.Length; c++)
                {
                    var cellText = cells[c].Trim();
                    if (string.IsNullOrEmpty(cellText) && (c == 0 || c == cells.Length - 1)) continue;
                    var cell = new Label(InlineFormat(cellText));
                    cell.enableRichText = true;
                    cell.AddToClassList("md-table-cell");
                    if (isHeader) cell.AddToClassList("md-table-cell-header");
                    row.Add(cell);
                }
                container.Add(row);
            }
            return container;
        }

        static VisualElement RenderBlockquote(Block block)
        {
            var container = new VisualElement();
            container.AddToClassList("md-blockquote");
            // Recursively render the blockquote content as markdown
            var inner = Render(block.Content);
            container.Add(inner);
            return container;
        }

        static VisualElement RenderHR()
        {
            var hr = new VisualElement();
            hr.AddToClassList("md-hr");
            return hr;
        }

        // ── Inline formatting ──

        static string InlineFormat(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Protect inline code spans from bold/italic processing
            var codeSpans = new List<string>();
            text = Regex.Replace(text, @"`([^`]+)`", m =>
            {
                codeSpans.Add(m.Groups[1].Value);
                return $"\x00CODE{codeSpans.Count - 1}\x00";
            });

            // Bold **text**
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");

            // Italic *text* (not preceded/followed by *)
            text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<i>$1</i>");

            // Links [text](url) → underlined text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "<u>$1</u>");

            // Restore inline code with distinct color
            for (int i = 0; i < codeSpans.Count; i++)
                text = text.Replace($"\x00CODE{i}\x00", $"<color=#E8A468>{codeSpans[i]}</color>");

            return text;
        }
    }
}
