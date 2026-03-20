using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("ClaudeCode.Editor.Tests")]

namespace ClaudeCode.Editor
{
    /// <summary>
    /// Handles permission requests from Claude Code when running without
    /// --dangerously-skip-permissions. Uses a file-based protocol:
    ///
    /// 1. Claude's PermissionRequest hook invokes a script.
    /// 2. Script writes a .request file to Temp/ClaudePermissions/.
    /// 3. Unity polls for .request files and shows EditorUtility.DisplayDialog.
    /// 4. Unity writes a .response file ("allow" or "deny").
    /// 5. Script reads it, returns the decision JSON, and exits.
    ///
    /// Every piece is short-lived and stateless — no server to crash.
    /// </summary>
    [InitializeOnLoad]
    public static class PermissionHandler
    {
        private static readonly string s_ProjectRoot;
        private static readonly string s_PermissionDir;
        private static readonly string s_HookScriptPath;
        private static readonly string s_HookSettingsPath;
        private static bool s_DialogOpen;

        /// <summary>
        /// Override this delegate in tests to avoid modal dialogs.
        /// Signature: (title, message) → true for Allow, false for Deny.
        /// </summary>
        internal static Func<string, string, bool> ShowDialog =
            (title, message) => EditorUtility.DisplayDialog(title, message, "Allow", "Deny");

        static PermissionHandler()
        {
            s_ProjectRoot = Path.GetDirectoryName(Application.dataPath);
            s_PermissionDir = Path.Combine(s_ProjectRoot, "Temp", "ClaudePermissions");

            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            s_HookScriptPath = Path.Combine(s_ProjectRoot, "Temp",
                isWindows ? "claude-permission-hook.ps1" : "claude-permission-hook.sh");
            s_HookSettingsPath = Path.Combine(s_ProjectRoot, "Temp", "claude-hook-settings.json");

            EnsureSetup();
            EnsureProjectRootSettings();
            FixSettingsPermissionCasing();
            // Don't clean up recent requests — a hook script may still be waiting.
            // Only remove truly abandoned files (older than the hook timeout + margin).
            CleanupStaleRequests();
            // Re-process any pending requests that survived the reload.
            EditorApplication.update += Poll;
        }

        /// <summary>Path to the JSON settings file that configures the PermissionRequest hook.
        /// Pass to Claude via <c>--settings "path"</c>.</summary>
        public static string HookSettingsPath => s_HookSettingsPath;

        /// <summary>Path where .request / .response files are exchanged.</summary>
        public static string PermissionDir => s_PermissionDir;

        /// <summary>Regenerates the hook script and settings file (idempotent).</summary>
        public static void EnsureSetup()
        {
            try
            {
                Directory.CreateDirectory(s_PermissionDir);
                WriteHookScript();
                WriteHookSettings();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClaudeCode] Failed to set up permission handler: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  Project-root settings (auto-created on first install)
        // ------------------------------------------------------------------

        private static readonly string[] k_RequiredPermissions = new[]
        {
            "Edit(Assets/**)",
            "Write(Assets/**)",
            "Edit(Packages/**)",
            "Write(Packages/**)",
        };

        /// <summary>
        /// Ensures the project root has a .claude/settings.json with baseline
        /// Unity permissions so users don't hit permission walls on first run.
        /// Only creates the file if it doesn't already exist — never overwrites
        /// a user's existing configuration.
        /// </summary>
        private static void EnsureProjectRootSettings()
        {
            try
            {
                string claudeDir = Path.Combine(s_ProjectRoot, ".claude");
                string settingsPath = Path.Combine(claudeDir, "settings.json");

                if (File.Exists(settingsPath)) return; // respect existing config

                Directory.CreateDirectory(claudeDir);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"permissions\": {");
                sb.AppendLine("    \"allow\": [");
                for (int i = 0; i < k_RequiredPermissions.Length; i++)
                {
                    string comma = i < k_RequiredPermissions.Length - 1 ? "," : "";
                    sb.AppendLine($"      \"{k_RequiredPermissions[i]}\"{comma}");
                }
                sb.AppendLine("    ]");
                sb.AppendLine("  }");
                sb.AppendLine("}");

                File.WriteAllText(settingsPath, sb.ToString(), Encoding.UTF8);
                Debug.Log("[ClaudeCode] Created .claude/settings.json with baseline Unity permissions " +
                    "(Edit/Write for Assets/** and Packages/**). Edit this file to customize.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClaudeCode] Could not create project root settings: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  Settings permission casing repair
        // ------------------------------------------------------------------

        /// <summary>
        /// Scans .claude/settings.json and .claude/settings.local.json for
        /// improperly-cased permission rules (e.g., "accept edits" instead of
        /// "Accept Edits"). Claude CLI requires tool names to start with
        /// uppercase and rejects the ENTIRE file on any violation — which also
        /// knocks out valid MCP server config in the same file.
        /// </summary>
        private static void FixSettingsPermissionCasing()
        {
            string claudeDir = Path.Combine(s_ProjectRoot, ".claude");
            FixPermissionCasingInFile(Path.Combine(claudeDir, "settings.json"));
            FixPermissionCasingInFile(Path.Combine(claudeDir, "settings.local.json"));
        }

        private static void FixPermissionCasingInFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string content = File.ReadAllText(path);
                string fixedContent = FixPermissionArrayCasing(content, "allow");
                fixedContent = FixPermissionArrayCasing(fixedContent, "deny");

                if (fixedContent != content)
                {
                    File.WriteAllText(path, fixedContent, Encoding.UTF8);
                    Debug.Log($"[ClaudeCode] Fixed permission rule casing in {Path.GetFileName(path)} " +
                        "(Claude CLI requires tool names to start with uppercase).");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClaudeCode] Could not validate {Path.GetFileName(path)}: {e.Message}");
            }
        }

        /// <summary>
        /// Finds a "key": ["rule1", "rule2"] array in JSON and title-cases
        /// the tool-name portion of each rule (the part before any parenthesis).
        /// </summary>
        internal static string FixPermissionArrayCasing(string json, string key)
        {
            var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\[([^\\]]*)\\]";
            var match = Regex.Match(json, pattern, RegexOptions.Singleline);
            if (!match.Success) return json;

            string arrayContent = match.Groups[1].Value;
            string fixedArray = Regex.Replace(arrayContent, "\"([^\"]+)\"", ruleMatch =>
            {
                string rule = ruleMatch.Groups[1].Value;
                return $"\"{TitleCaseToolName(rule)}\"";
            });

            if (fixedArray == arrayContent) return json;

            return json.Substring(0, match.Groups[1].Index)
                + fixedArray
                + json.Substring(match.Groups[1].Index + match.Groups[1].Length);
        }

        /// <summary>
        /// Title-cases the tool name portion of a permission rule.
        /// "accept edits" → "Accept Edits",
        /// "edit(Assets/**)" → "Edit(Assets/**)",
        /// "Write(Assets/**)" → unchanged.
        /// </summary>
        internal static string TitleCaseToolName(string rule)
        {
            int parenIdx = rule.IndexOf('(');
            string toolPart = parenIdx >= 0 ? rule.Substring(0, parenIdx) : rule;
            string rest = parenIdx >= 0 ? rule.Substring(parenIdx) : "";

            var words = toolPart.Split(' ');
            bool changed = false;
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0 && char.IsLower(words[i][0]))
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
                    changed = true;
                }
            }

            return changed ? string.Join(" ", words) + rest : rule;
        }

        // ------------------------------------------------------------------
        //  Hook script generation
        // ------------------------------------------------------------------

        private static void WriteHookScript()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                WriteWindowsHookScript();
            else
                WriteUnixHookScript();
        }

        private static void WriteWindowsHookScript()
        {
            // PowerShell script — single quotes around the directory to avoid escaping.
            string escapedDir = s_PermissionDir.Replace("'", "''");
            var sb = new StringBuilder();
            sb.AppendLine("# Claude Code Permission Hook — generated by Unity Claude Code CLI");
            sb.AppendLine("# Do not edit; regenerated on every domain reload.");
            sb.AppendLine($"$permDir = '{escapedDir}'");
            sb.AppendLine("$id = [guid]::NewGuid().ToString()");
            sb.AppendLine("$requestFile  = Join-Path $permDir \"$id.request\"");
            sb.AppendLine("$responseFile = Join-Path $permDir \"$id.response\"");
            sb.AppendLine();
            sb.AppendLine("# Read hook input from Claude on stdin");
            sb.AppendLine("$inputJson = [Console]::In.ReadToEnd()");
            sb.AppendLine();
            sb.AppendLine("# Write request for Unity to pick up");
            sb.AppendLine("if (-not (Test-Path $permDir)) { New-Item -ItemType Directory -Force -Path $permDir | Out-Null }");
            sb.AppendLine("Set-Content -Path $requestFile -Value $inputJson -Encoding UTF8");
            sb.AppendLine();
            sb.AppendLine("# Poll for response (timeout 120 s)");
            sb.AppendLine("$elapsed = 0");
            sb.AppendLine("while ($elapsed -lt 120) {");
            sb.AppendLine("    if (Test-Path $responseFile) {");
            sb.AppendLine("        $raw = (Get-Content $responseFile -Raw).Trim()");
            sb.AppendLine("        Remove-Item $requestFile  -ErrorAction SilentlyContinue");
            sb.AppendLine("        Remove-Item $responseFile -ErrorAction SilentlyContinue");
            sb.AppendLine("        if ($raw -eq 'allow') {");
            sb.AppendLine("            Write-Output '{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"allow\"}}}'");
            sb.AppendLine("        } else {");
            sb.AppendLine("            Write-Output '{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"deny\",\"message\":\"Denied by user in Unity Editor\"}}}'");
            sb.AppendLine("        }");
            sb.AppendLine("        exit 0");
            sb.AppendLine("    }");
            sb.AppendLine("    Start-Sleep -Milliseconds 500");
            sb.AppendLine("    $elapsed += 0.5");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("# Timeout — deny");
            sb.AppendLine("Remove-Item $requestFile -ErrorAction SilentlyContinue");
            sb.AppendLine("Write-Output '{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"deny\",\"message\":\"Timed out waiting for Unity Editor\"}}}'");
            sb.AppendLine("exit 0");

            File.WriteAllText(s_HookScriptPath, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteUnixHookScript()
        {
            var sb = new StringBuilder();
            sb.AppendLine("#!/bin/bash");
            sb.AppendLine("# Claude Code Permission Hook — generated by Unity Claude Code CLI");
            sb.AppendLine($"PERM_DIR='{s_PermissionDir.Replace("'", "'\\''")}'");
            sb.AppendLine("ID=$(uuidgen 2>/dev/null || cat /proc/sys/kernel/random/uuid 2>/dev/null || echo $$-$(date +%s))");
            sb.AppendLine("REQUEST_FILE=\"$PERM_DIR/$ID.request\"");
            sb.AppendLine("RESPONSE_FILE=\"$PERM_DIR/$ID.response\"");
            sb.AppendLine();
            sb.AppendLine("INPUT_JSON=$(cat)");
            sb.AppendLine("mkdir -p \"$PERM_DIR\"");
            sb.AppendLine("printf '%s' \"$INPUT_JSON\" > \"$REQUEST_FILE\"");
            sb.AppendLine();
            sb.AppendLine("ELAPSED=0");
            sb.AppendLine("while [ $ELAPSED -lt 240 ]; do");
            sb.AppendLine("    if [ -f \"$RESPONSE_FILE\" ]; then");
            sb.AppendLine("        RAW=$(tr -d '\\n' < \"$RESPONSE_FILE\")");
            sb.AppendLine("        rm -f \"$REQUEST_FILE\" \"$RESPONSE_FILE\"");
            sb.AppendLine("        if [ \"$RAW\" = \"allow\" ]; then");
            sb.AppendLine("            echo '{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"allow\"}}}'");
            sb.AppendLine("        else");
            sb.AppendLine("            echo '{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"deny\",\"message\":\"Denied by user in Unity Editor\"}}}'");
            sb.AppendLine("        fi");
            sb.AppendLine("        exit 0");
            sb.AppendLine("    fi");
            sb.AppendLine("    sleep 0.5");
            sb.AppendLine("    ELAPSED=$((ELAPSED + 1))");
            sb.AppendLine("done");
            sb.AppendLine();
            sb.AppendLine("rm -f \"$REQUEST_FILE\"");
            sb.AppendLine("echo '{\"hookSpecificOutput\":{\"hookEventName\":\"PermissionRequest\",\"decision\":{\"behavior\":\"deny\",\"message\":\"Timed out waiting for Unity Editor\"}}}'");
            sb.AppendLine("exit 0");

            File.WriteAllText(s_HookScriptPath, sb.ToString(), new UTF8Encoding(false));

            try
            {
                using (var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod", Arguments = $"+x \"{s_HookScriptPath}\"",
                    UseShellExecute = false, CreateNoWindow = true
                }))
                { proc?.WaitForExit(5000); }
            }
            catch { /* best-effort */ }
        }

        // ------------------------------------------------------------------
        //  Hook settings file (passed to Claude via --settings)
        // ------------------------------------------------------------------

        private static void WriteHookSettings()
        {
            string command;
            if (Application.platform == RuntimePlatform.WindowsEditor)
                command = $"powershell -ExecutionPolicy Bypass -File \"{s_HookScriptPath}\"";
            else
                command = s_HookScriptPath;

            string jsonCommand = JsonEscape(command);

            string json =
                "{\n" +
                "  \"hooks\": {\n" +
                "    \"PermissionRequest\": [\n" +
                "      {\n" +
                "        \"type\": \"command\",\n" +
                $"        \"command\": \"{jsonCommand}\",\n" +
                "        \"timeout\": 125000\n" +
                "      }\n" +
                "    ]\n" +
                "  }\n" +
                "}";

            File.WriteAllText(s_HookSettingsPath, json, Encoding.UTF8);
        }

        // ------------------------------------------------------------------
        //  Polling & dialog
        // ------------------------------------------------------------------

        private static void Poll()
        {
            if (s_DialogOpen) return; // one dialog at a time
            if (!Directory.Exists(s_PermissionDir)) return;

            string[] requestFiles;
            try { requestFiles = Directory.GetFiles(s_PermissionDir, "*.request"); }
            catch { return; }

            if (requestFiles.Length == 0) return;

            string requestFile = requestFiles[0];
            string id = Path.GetFileNameWithoutExtension(requestFile);
            string responseFile = Path.Combine(s_PermissionDir, id + ".response");

            if (File.Exists(responseFile)) return; // already handled

            try
            {
                string json = File.ReadAllText(requestFile);

                string toolName = ExtractJsonString(json, "tool_name") ?? "Unknown tool";
                string detail = BuildDetailString(json);

                string message = string.IsNullOrEmpty(detail)
                    ? $"Claude wants to use: {toolName}"
                    : $"Claude wants to use: {toolName}\n\n{detail}";

                s_DialogOpen = true;
                bool allowed = ShowDialog("Claude Permission Request", message);
                s_DialogOpen = false;

                File.WriteAllText(responseFile, allowed ? "allow" : "deny");
            }
            catch (Exception e)
            {
                s_DialogOpen = false;
                Debug.LogWarning($"[ClaudeCode] Permission handler error: {e.Message}");
                try { File.WriteAllText(responseFile, "deny"); } catch { /* unrecoverable */ }
            }
        }

        // ------------------------------------------------------------------
        //  Cleanup
        // ------------------------------------------------------------------

        private static void CleanupStaleRequests()
        {
            if (!Directory.Exists(s_PermissionDir)) return;
            try
            {
                // Only remove files older than the hook timeout (125 s) + generous margin.
                // This guarantees the hook script has already timed out and won't need
                // the file. Younger files may be from an in-flight request whose dialog
                // was interrupted by domain reload — Poll will re-show the dialog for those.
                var cutoff = DateTime.UtcNow.AddMinutes(-5);
                foreach (string file in Directory.GetFiles(s_PermissionDir))
                {
                    if (File.GetCreationTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch { /* best-effort */ }
        }

        // ------------------------------------------------------------------
        //  JSON helpers (lightweight — avoids Newtonsoft dependency)
        // ------------------------------------------------------------------

        internal static string BuildDetailString(string json)
        {
            string toolInput = ExtractJsonObject(json, "tool_input");
            if (toolInput == null) return "";

            string filePath = ExtractJsonString(toolInput, "file_path");
            if (!string.IsNullOrEmpty(filePath)) return filePath;

            string command = ExtractJsonString(toolInput, "command");
            if (!string.IsNullOrEmpty(command))
                return command.Length > 300 ? command.Substring(0, 300) + "..." : command;

            string pattern = ExtractJsonString(toolInput, "pattern");
            if (!string.IsNullOrEmpty(pattern)) return $"Pattern: {pattern}";

            string url = ExtractJsonString(toolInput, "url");
            if (!string.IsNullOrEmpty(url)) return url;

            return "";
        }

        internal static string ExtractJsonString(string json, string key)
        {
            var match = Regex.Match(json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return null;
            return UnescapeJsonString(match.Groups[1].Value);
        }

        /// <summary>
        /// Character-by-character JSON string unescaping. Sequential Replace()
        /// can't correctly handle sequences like \\n (literal backslash + n)
        /// vs \n (newline), so we walk the string once.
        /// </summary>
        private static string UnescapeJsonString(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"':  sb.Append('"');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'r':  sb.Append('\r'); break;
                        case '/':  sb.Append('/');  break;
                        default:   sb.Append('\\'); sb.Append(next); break;
                    }
                    i++; // consumed the escape pair
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        internal static string ExtractJsonObject(string json, string key)
        {
            var match = Regex.Match(json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\\{{");
            if (!match.Success) return null;

            int start = match.Index + match.Length - 1;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        private static string JsonEscape(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
