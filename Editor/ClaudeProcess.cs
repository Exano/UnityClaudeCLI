using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ClaudeCode.Editor
{
    public enum PermissionMode { Default, AutoApprove, Plan }

    public class ClaudeProcess : IDisposable
    {
        public enum ProcessState { Idle, Running, Error }

        public struct OutputChunk
        {
            public enum Kind { Text, ToolUse, Status, Result, System, Thinking, Complete }
            public Kind Type;
            public string Text;
        }

        public ProcessState CurrentState { get; private set; } = ProcessState.Idle;
        public string LastSessionId { get; set; }

        /// <summary>True if the OS process has exited (safe to call anytime).</summary>
        public bool HasProcessExited
        {
            get { try { return _process == null || _process.HasExited; } catch { return true; } }
        }

        private Process _process;
        private readonly ConcurrentQueue<OutputChunk> _queue = new ConcurrentQueue<OutputChunk>();
        private CancellationTokenSource _cts;
        private readonly string _workingDirectory;

        [Serializable] private class JsonBase { public string type; public string subtype; }
        [Serializable] private class JsonSystem { public string type; public string session_id; public string model; }
        [Serializable] private class JsonContentBlock { public string type; public string text; public string name; public string thinking; }
        [Serializable] private class JsonMessage { public string role; public JsonContentBlock[] content; }
        [Serializable] private class JsonAssistant { public string type; public JsonMessage message; public string session_id; }
        [Serializable] private class JsonUsage { public int input_tokens; public int output_tokens; }
        [Serializable] private class JsonResult
        {
            public string type; public string subtype; public string result;
            public string session_id; public float cost_usd; public float total_cost_usd;
            public int duration_ms; public int num_turns; public bool is_error;
            public JsonUsage usage;
            public string[] errors;
        }

        public ClaudeProcess(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        public bool IsClaudeAvailable()
        {
            try
            {
                var psi = CreateShellProcessInfo("claude --version");
                psi.RedirectStandardInput = false;
                psi.Environment.Remove("CLAUDECODE");
                psi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                    return proc.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Resolves the MCP endpoint URL, checking (in order):
        /// 1. ClaudeCode.McpUrl (our settings panel)
        /// 2. MCPForUnity.HttpUrl (unity-mcp package)
        /// 3. Default http://127.0.0.1:8080
        /// Always returns a URL ending with /mcp.
        /// </summary>
        public static string GetMcpUrl()
        {
            var baseUrl = GetMcpBaseUrl();
            baseUrl = baseUrl.TrimEnd('/');
            if (baseUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
                return baseUrl;
            return baseUrl + "/mcp";
        }

        /// <summary>Returns the MCP base URL (without /mcp suffix).</summary>
        public static string GetMcpBaseUrl()
        {
            const string ownKey = "ClaudeCode.McpUrl";
            const string legacyKey = "MCPForUnity.HttpUrl";
            const string defaultBase = "http://127.0.0.1:8080";

            var url = UnityEditor.EditorPrefs.GetString(ownKey, "");
            if (string.IsNullOrEmpty(url))
                url = UnityEditor.EditorPrefs.GetString(legacyKey, "");
            if (string.IsNullOrEmpty(url))
                url = defaultBase;

            url = url.TrimEnd('/');
            if (url.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - 4);
            return url;
        }

        /// <summary>
        /// Checks whether the Unity MCP server is registered with Claude CLI
        /// and registers it automatically if not. Returns a status message
        /// or null if already registered.
        /// </summary>
        public string EnsureMcpRegistered(string mcpUrl = null)
        {
            if (mcpUrl == null)
                mcpUrl = GetMcpUrl();
            try
            {
                // Check if already registered
                var listPsi = CreateShellProcessInfo("claude mcp list");
                listPsi.RedirectStandardInput = false;
                listPsi.Environment.Remove("CLAUDECODE");
                listPsi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");
                string listOutput;
                using (var proc = Process.Start(listPsi))
                {
                    listOutput = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(10000);
                }

                // Look for any existing unity MCP entry pointing at our URL
                if (listOutput != null && listOutput.Contains(mcpUrl))
                    return null; // already registered

                // Register the MCP server
                var addCmd = $"claude mcp add --scope local --transport http unity {mcpUrl}";
                var addPsi = CreateShellProcessInfo(addCmd);
                addPsi.RedirectStandardInput = false;
                addPsi.Environment.Remove("CLAUDECODE");
                addPsi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");
                using (var proc = Process.Start(addPsi))
                {
                    proc.WaitForExit(10000);
                    if (proc.ExitCode == 0)
                        return $"Registered Unity MCP server at {mcpUrl}";
                    var err = proc.StandardError.ReadToEnd();
                    return $"Failed to register MCP server: {err?.Trim()}";
                }
            }
            catch (Exception e)
            {
                return $"MCP registration check failed: {e.Message}";
            }
        }

        internal static string BuildFlags(PermissionMode permissionMode, string model,
            int maxTurns, bool resume, string sessionId, string hookSettingsPath)
        {
            var flags = "--output-format stream-json --verbose";
            switch (permissionMode)
            {
                case PermissionMode.AutoApprove:
                    flags += " --dangerously-skip-permissions";
                    break;
                case PermissionMode.Plan:
                    flags += " --permission-mode plan";
                    break;
                case PermissionMode.Default:
                    if (!string.IsNullOrEmpty(hookSettingsPath) && File.Exists(hookSettingsPath))
                        flags += $" --settings \"{hookSettingsPath}\"";
                    break;
            }
            if (!string.IsNullOrEmpty(model))
                flags += $" --model {model}";
            if (maxTurns > 0)
                flags += $" --max-turns {maxTurns}";
            if (resume && !string.IsNullOrEmpty(sessionId)
                && Guid.TryParse(sessionId, out _))
                flags += $" --continue {sessionId}";
            return flags;
        }

        public void SendMessage(string prompt, bool resume = false,
            PermissionMode permissionMode = PermissionMode.AutoApprove,
            string model = null, int maxTurns = 0)
        {
            if (CurrentState == ProcessState.Running)
            {
                Enqueue(OutputChunk.Kind.System, "A request is already running.");
                return;
            }

            CurrentState = ProcessState.Running;
            _cts = new CancellationTokenSource();

            try
            {
                var flags = BuildFlags(permissionMode, model, maxTurns, resume,
                    LastSessionId, PermissionHandler.HookSettingsPath);

                // Always use cmd.exe/bash with stdin pipe — claude -p is non-interactive
                var psi = CreateShellProcessInfo($"claude -p {flags}");
                psi.RedirectStandardInput = true;

                psi.Environment["NO_COLOR"] = "1";
                psi.Environment.Remove("CLAUDECODE");
                psi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");

                _process = new Process { StartInfo = psi };
                _process.Start();

                // Write prompt and close stdin to signal EOF
                _process.StandardInput.WriteLine(prompt);
                _process.StandardInput.Flush();
                _process.StandardInput.Close();

                var ct = _cts.Token;
                var stdoutTask = ReadStdoutAsync(_process.StandardOutput, ct);
                var stderrTask = ReadStderrAsync(_process.StandardError, ct);

                Task.Run(async () =>
                {
                    try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
                    int exitCode = -1;
                    try { if (_process != null && _process.HasExited) exitCode = _process.ExitCode; } catch { }
                    CurrentState = exitCode != 0 ? ProcessState.Error : ProcessState.Idle;
                    Enqueue(OutputChunk.Kind.Complete, null);
                });
            }
            catch (Exception e)
            {
                CurrentState = ProcessState.Error;
                Enqueue(OutputChunk.Kind.System, $"Failed to start claude: {e.Message}");
                Enqueue(OutputChunk.Kind.Complete, null);
            }
        }

        public void Cancel()
        {
            if (CurrentState != ProcessState.Running) return;
            _cts?.Cancel();
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        using (var kill = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {_process.Id}",
                            UseShellExecute = false, CreateNoWindow = true,
                        })) { kill?.WaitForExit(5000); }
                    }
                    else { _process.Kill(); }
                }
            }
            catch { }
            CurrentState = ProcessState.Idle;
            Enqueue(OutputChunk.Kind.System, "[Cancelled]");
            Enqueue(OutputChunk.Kind.Complete, null);
        }

        public bool TryDequeue(out OutputChunk chunk) => _queue.TryDequeue(out chunk);

        public void Dispose()
        {
            if (CurrentState == ProcessState.Running) Cancel();
            try { _process?.Dispose(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _process = null; _cts = null;
        }

        private void Enqueue(OutputChunk.Kind kind, string text) =>
            _queue.Enqueue(new OutputChunk { Type = kind, Text = text });

        private async Task ReadStdoutAsync(StreamReader reader, CancellationToken ct)
        {
            try
            {
                string line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                    ParseStreamJsonLine(line);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Enqueue(OutputChunk.Kind.System, $"[Read error: {e.Message}]"); }
        }

        private async Task ReadStderrAsync(StreamReader reader, CancellationToken ct)
        {
            try
            {
                string line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        Enqueue(OutputChunk.Kind.System, line.Trim());
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private void ParseStreamJsonLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            line = line.Trim();
            if (!line.StartsWith("{"))
            {
                // Suppress bare UUIDs (session IDs) that the CLI sometimes emits before JSON
                if (line.Length > 0 && !Guid.TryParse(line, out _))
                    Enqueue(OutputChunk.Kind.Text, line + "\n");
                return;
            }
            try
            {
                var baseMsg = JsonUtility.FromJson<JsonBase>(line);
                switch (baseMsg.type)
                {
                    case "system": HandleSystemEvent(line); break;
                    case "assistant": HandleAssistantEvent(line); break;
                    case "result": HandleResultEvent(line); break;
                    case "user": HandleUserEvent(line); break;
                }
            }
            catch { Enqueue(OutputChunk.Kind.Text, line + "\n"); }
        }

        private void HandleSystemEvent(string json)
        {
            try
            {
                var msg = JsonUtility.FromJson<JsonSystem>(json);
                LastSessionId = msg.session_id;
                if (!string.IsNullOrEmpty(msg.model))
                    Enqueue(OutputChunk.Kind.Status, $"Model: {msg.model}");
            }
            catch { }
        }

        private void HandleAssistantEvent(string json)
        {
            try
            {
                var msg = JsonUtility.FromJson<JsonAssistant>(json);
                if (msg.message?.content == null) return;
                var text = new StringBuilder();
                foreach (var block in msg.message.content)
                {
                    if (block.type == "text" && !string.IsNullOrEmpty(block.text))
                        text.Append(block.text);
                    else if (block.type == "tool_use" && !string.IsNullOrEmpty(block.name))
                        Enqueue(OutputChunk.Kind.ToolUse, block.name);
                    else if (block.type == "thinking" && !string.IsNullOrEmpty(block.thinking))
                        Enqueue(OutputChunk.Kind.Thinking, block.thinking);
                }
                if (text.Length > 0)
                    Enqueue(OutputChunk.Kind.Text, text.ToString());
            }
            catch { }
        }

        internal static string ExtractToolInputQuestion(string json)
        {
            // The question lives in: "input":{"question":"..."} inside a tool_use block.
            var match = System.Text.RegularExpressions.Regex.Match(json,
                "\"question\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return null;
            return match.Groups[1].Value
                .Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private void HandleResultEvent(string json)
        {
            try
            {
                var msg = JsonUtility.FromJson<JsonResult>(json);
                LastSessionId = msg.session_id;
                var info = new StringBuilder();
                float cost = msg.total_cost_usd > 0 ? msg.total_cost_usd : msg.cost_usd;
                if (cost > 0) info.Append($"${cost:F4}");
                if (msg.usage != null)
                {
                    int total = msg.usage.input_tokens + msg.usage.output_tokens;
                    if (total > 0)
                    {
                        if (info.Length > 0) info.Append("  |  ");
                        info.Append($"{msg.usage.input_tokens} in / {msg.usage.output_tokens} out");
                    }
                }
                if (msg.duration_ms > 0)
                {
                    if (info.Length > 0) info.Append("  |  ");
                    info.Append($"{msg.duration_ms / 1000f:F1}s");
                }
                if (msg.num_turns > 0)
                {
                    if (info.Length > 0) info.Append("  |  ");
                    info.Append($"{msg.num_turns} turn{(msg.num_turns != 1 ? "s" : "")}");
                }
                if (info.Length > 0) Enqueue(OutputChunk.Kind.Result, info.ToString());
                if (msg.is_error)
                {
                    if (!string.IsNullOrEmpty(msg.result))
                        Enqueue(OutputChunk.Kind.System, $"[Error] {msg.result}");
                    if (msg.errors != null)
                        foreach (var err in msg.errors)
                            if (!string.IsNullOrEmpty(err))
                                Enqueue(OutputChunk.Kind.System, $"[Error] {err}");
                }
            }
            catch { }
        }

        private void HandleUserEvent(string json)
        {
            // tool_use_result events show what tools did. Extract file paths from the JSON.
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(json,
                    "\"tool_use_result\":\\{\"type\":\"(\\w+)\",\"filePath\":\"([^\"]+)\"");
                if (match.Success)
                {
                    var op = match.Groups[1].Value;   // "create", "edit", etc.
                    var path = match.Groups[2].Value;
                    Enqueue(OutputChunk.Kind.Status, $"{op}: {path}");
                }
            }
            catch { }
        }

        private ProcessStartInfo CreateShellProcessInfo(string command)
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = _workingDirectory,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            if (Application.platform == RuntimePlatform.WindowsEditor)
            { psi.FileName = "cmd.exe"; psi.Arguments = $"/c {command}"; }
            else
            { psi.FileName = "/bin/bash"; psi.Arguments = $"-l -c '{command}'"; }
            return psi;
        }

    }
}
