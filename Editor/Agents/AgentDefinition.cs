using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeCode.Editor.Agents
{
    /// <summary>
    /// A parsed agent: name, keywords for auto-detection, and instruction content.
    /// </summary>
    public class AgentDefinition
    {
        public string Name;
        public string FilePath;
        public string[] Keywords = Array.Empty<string>();
        public string Content = "";
        public bool IsBuiltIn;

        /// <summary>
        /// Score this agent against a user prompt. Returns the number of keyword hits.
        /// Uses word-boundary matching for short keywords to avoid false positives.
        /// </summary>
        public int ScorePrompt(string prompt)
        {
            if (Keywords.Length == 0) return 0;
            var lower = prompt.ToLowerInvariant();
            int hits = 0;
            foreach (var kw in Keywords)
            {
                var kwLower = kw.ToLowerInvariant();
                // Short keywords (<=3 chars like "UI") need word boundary matching
                if (kwLower.Length <= 3)
                {
                    if (Regex.IsMatch(lower, $@"\b{Regex.Escape(kwLower)}\b"))
                        hits++;
                }
                else
                {
                    if (lower.Contains(kwLower))
                        hits++;
                }
            }
            return hits;
        }

        /// <summary>
        /// Parse a .md file with optional YAML-style frontmatter.
        /// </summary>
        public static AgentDefinition Parse(string filePath, bool isBuiltIn)
        {
            var agent = new AgentDefinition
            {
                FilePath = filePath,
                IsBuiltIn = isBuiltIn,
                Name = Path.GetFileNameWithoutExtension(filePath)
            };

            if (!File.Exists(filePath)) return agent;

            var text = File.ReadAllText(filePath);
            var (frontmatter, body) = ExtractFrontmatter(text);

            agent.Content = body.Trim();

            if (frontmatter != null)
            {
                agent.Name = ParseField(frontmatter, "name") ?? agent.Name;
                var kwStr = ParseField(frontmatter, "keywords");
                if (kwStr != null)
                    agent.Keywords = ParseList(kwStr);
            }

            return agent;
        }

        static (string frontmatter, string body) ExtractFrontmatter(string text)
        {
            if (!text.StartsWith("---"))
                return (null, text);

            var endIdx = text.IndexOf("---", 3, StringComparison.Ordinal);
            if (endIdx < 0)
                return (null, text);

            var fm = text.Substring(3, endIdx - 3).Trim();
            var body = text.Substring(endIdx + 3);
            return (fm, body);
        }

        static string ParseField(string frontmatter, string key)
        {
            // Simple YAML: "key: value" or "key: [a, b, c]"
            var match = Regex.Match(frontmatter,
                $@"^{Regex.Escape(key)}\s*:\s*(.+)$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        static string[] ParseList(string value)
        {
            // Handle [a, b, c] format
            value = value.Trim();
            if (value.StartsWith("[") && value.EndsWith("]"))
                value = value.Substring(1, value.Length - 2);

            var parts = value.Split(',');
            var result = new List<string>();
            foreach (var p in parts)
            {
                var trimmed = p.Trim().Trim('"', '\'');
                if (trimmed.Length > 0)
                    result.Add(trimmed);
            }
            return result.ToArray();
        }
    }
}
