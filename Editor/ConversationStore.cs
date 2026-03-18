using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ClaudeCode.Editor
{
    [Serializable]
    public class ConversationData
    {
        public string id;
        public string sessionId;
        public string title;
        public string createdAt;
        public string updatedAt;
        public List<ClaudeCodeEditorWindow.ChatMessage> messages = new List<ClaudeCodeEditorWindow.ChatMessage>();
    }

    public static class ConversationStore
    {
        private static string StorePath
        {
            get
            {
                var path = Path.Combine(Application.dataPath, "..", "Library", "ClaudeCode", "Conversations");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static void Save(ConversationData data)
        {
            if (data == null || string.IsNullOrEmpty(data.id)) return;
            if (data.messages == null || data.messages.Count == 0) return;

            if (string.IsNullOrEmpty(data.createdAt))
                data.createdAt = DateTime.Now.ToString("o");
            data.updatedAt = DateTime.Now.ToString("o");

            if (string.IsNullOrEmpty(data.title))
                data.title = GenerateTitle(data.messages);

            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(Path.Combine(StorePath, $"{data.id}.json"), json);
        }

        public static ConversationData Load(string id)
        {
            var path = Path.Combine(StorePath, $"{id}.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonUtility.FromJson<ConversationData>(json);
        }

        public static List<ConversationData> ListAll()
        {
            var results = new List<ConversationData>();
            var dir = StorePath;
            if (!Directory.Exists(dir)) return results;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonUtility.FromJson<ConversationData>(json);
                    if (data != null && !string.IsNullOrEmpty(data.id))
                        results.Add(data);
                }
                catch { /* skip corrupted files */ }
            }

            results.Sort((a, b) => string.Compare(b.updatedAt, a.updatedAt, StringComparison.Ordinal));
            return results;
        }

        public static void Delete(string id)
        {
            var path = Path.Combine(StorePath, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static string GenerateTitle(List<ClaudeCodeEditorWindow.ChatMessage> messages)
        {
            foreach (var msg in messages)
            {
                if (msg.role == ClaudeCodeEditorWindow.ChatMessage.Role.User)
                {
                    var text = msg.text;
                    // Remove attachment/agent indicators from display text
                    var idx = text.IndexOf("  [");
                    if (idx > 0) text = text.Substring(0, idx);
                    text = text.Trim();
                    if (text.Length > 50) text = text.Substring(0, 47) + "\u2026";
                    return text;
                }
            }
            return "New Chat";
        }
    }
}
