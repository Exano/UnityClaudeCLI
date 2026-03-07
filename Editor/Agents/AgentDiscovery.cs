using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ClaudeCode.Editor.Agents
{
    /// <summary>
    /// Discovers agent .md files from the package (built-in) and project (custom) folders.
    /// Project agents with the same name override built-in agents.
    /// </summary>
    public static class AgentDiscovery
    {
        private const string k_PackageAgentsPath = "Packages/com.tonythedev.unity-claude-code-cli/Editor/Agents/Definitions";
        private const string k_ProjectAgentsPath = "Assets/Docs/Agents";

        private static List<AgentDefinition> s_cachedAgents;
        private static AgentDefinition s_cachedBase;

        /// <summary>
        /// Returns _Base agent (always included). Null if no _Base.md exists.
        /// </summary>
        public static AgentDefinition GetBase()
        {
            if (s_cachedBase == null) Refresh();
            return s_cachedBase;
        }

        /// <summary>
        /// Returns all selectable agents (excludes _Base).
        /// </summary>
        public static List<AgentDefinition> GetAgents()
        {
            if (s_cachedAgents == null) Refresh();
            return s_cachedAgents;
        }

        /// <summary>
        /// Re-scan both directories.
        /// </summary>
        public static void Refresh()
        {
            var byName = new Dictionary<string, AgentDefinition>();
            s_cachedBase = null;

            // 1. Scan built-in agents from package
            ScanDirectory(k_PackageAgentsPath, true, byName);

            // 2. Scan project agents — overrides built-in by name
            ScanDirectory(k_ProjectAgentsPath, false, byName);

            s_cachedAgents = new List<AgentDefinition>(byName.Values);
            s_cachedAgents.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        /// <summary>
        /// Auto-detect agents for a prompt. Returns agents with at least 1 keyword hit,
        /// sorted by score descending.
        /// </summary>
        public static List<AgentDefinition> AutoDetect(string prompt, int maxAgents = 3)
        {
            var agents = GetAgents();
            var scored = new List<(AgentDefinition agent, int score)>();

            foreach (var a in agents)
            {
                int score = a.ScorePrompt(prompt);
                if (score > 0)
                    scored.Add((a, score));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            var result = new List<AgentDefinition>();
            int count = scored.Count < maxAgents ? scored.Count : maxAgents;
            for (int i = 0; i < count; i++)
                result.Add(scored[i].agent);
            return result;
        }

        /// <summary>
        /// Build the full system context from base + selected agents.
        /// </summary>
        public static string BuildContext(List<AgentDefinition> selectedAgents)
        {
            var sb = new System.Text.StringBuilder();
            var baseAgent = GetBase();
            if (baseAgent != null && baseAgent.Content.Length > 0)
            {
                sb.AppendLine(baseAgent.Content);
                sb.AppendLine();
            }

            foreach (var a in selectedAgents)
            {
                if (a.Content.Length > 0)
                {
                    sb.AppendLine($"## {a.Name} Guidelines");
                    sb.AppendLine(a.Content);
                    sb.AppendLine();
                }
            }

            return sb.ToString().TrimEnd();
        }

        static void ScanDirectory(string relativePath, bool isBuiltIn,
            Dictionary<string, AgentDefinition> byName)
        {
            // Convert Unity-style path to absolute
            var fullPath = Path.GetFullPath(relativePath);
            if (!Directory.Exists(fullPath)) return;

            foreach (var file in Directory.GetFiles(fullPath, "*.md"))
            {
                var agent = AgentDefinition.Parse(file, isBuiltIn);
                if (agent.Name == "_Base")
                {
                    // Project _Base overrides package _Base
                    if (!isBuiltIn || s_cachedBase == null)
                        s_cachedBase = agent;
                }
                else
                {
                    // Project agents override built-in agents with same name
                    var key = agent.Name.ToLowerInvariant();
                    if (!isBuiltIn || !byName.ContainsKey(key))
                        byName[key] = agent;
                }
            }
        }
    }
}
