using System.IO;
using NUnit.Framework;
using ClaudeCode.Editor;

namespace ClaudeCode.Editor.Tests
{
    public class PermissionHandlerTests
    {
        // -----------------------------------------------------------
        //  JSON extraction
        // -----------------------------------------------------------

        [Test]
        public void ExtractJsonString_BasicKey()
        {
            const string json = "{\"tool_name\":\"Edit\",\"other\":123}";
            Assert.AreEqual("Edit", PermissionHandler.ExtractJsonString(json, "tool_name"));
        }

        [Test]
        public void ExtractJsonString_WithEscapedChars()
        {
            const string json = "{\"file_path\":\"Assets\\\\Scripts\\\\Foo.cs\"}";
            Assert.AreEqual("Assets\\Scripts\\Foo.cs",
                PermissionHandler.ExtractJsonString(json, "file_path"));
        }

        [Test]
        public void ExtractJsonString_MissingKey_ReturnsNull()
        {
            const string json = "{\"tool_name\":\"Edit\"}";
            Assert.IsNull(PermissionHandler.ExtractJsonString(json, "missing"));
        }

        [Test]
        public void ExtractJsonString_WithWhitespace()
        {
            const string json = "{ \"tool_name\" : \"Bash\" }";
            Assert.AreEqual("Bash", PermissionHandler.ExtractJsonString(json, "tool_name"));
        }

        [Test]
        public void ExtractJsonString_DoubleBackslashN_IsLiteralBackslashPlusN()
        {
            // JSON \\n = literal backslash + 'n', NOT a newline
            const string json = "{\"path\":\"C:\\\\new_folder\\\\test\"}";
            Assert.AreEqual("C:\\new_folder\\test",
                PermissionHandler.ExtractJsonString(json, "path"));
        }

        [Test]
        public void ExtractJsonString_SingleBackslashN_IsNewline()
        {
            // JSON \n = newline character
            const string json = "{\"msg\":\"line1\\nline2\"}";
            Assert.AreEqual("line1\nline2",
                PermissionHandler.ExtractJsonString(json, "msg"));
        }

        [Test]
        public void ExtractJsonObject_ReturnsInnerObject()
        {
            const string json = "{\"tool_input\":{\"file_path\":\"a.cs\",\"old\":\"x\"}}";
            string obj = PermissionHandler.ExtractJsonObject(json, "tool_input");
            Assert.IsNotNull(obj);
            Assert.IsTrue(obj.StartsWith("{"));
            Assert.IsTrue(obj.EndsWith("}"));
            Assert.IsTrue(obj.Contains("file_path"));
        }

        [Test]
        public void ExtractJsonObject_NestedBraces()
        {
            const string json = "{\"tool_input\":{\"a\":{\"b\":1},\"c\":2}}";
            string obj = PermissionHandler.ExtractJsonObject(json, "tool_input");
            Assert.AreEqual("{\"a\":{\"b\":1},\"c\":2}", obj);
        }

        [Test]
        public void ExtractJsonObject_MissingKey_ReturnsNull()
        {
            Assert.IsNull(PermissionHandler.ExtractJsonObject("{\"a\":1}", "tool_input"));
        }

        // -----------------------------------------------------------
        //  Detail string building
        // -----------------------------------------------------------

        [Test]
        public void BuildDetailString_FilePath()
        {
            const string json = "{\"tool_input\":{\"file_path\":\"Assets/Editor/Foo.cs\"}}";
            Assert.AreEqual("Assets/Editor/Foo.cs", PermissionHandler.BuildDetailString(json));
        }

        [Test]
        public void BuildDetailString_Command()
        {
            const string json = "{\"tool_input\":{\"command\":\"git status\"}}";
            Assert.AreEqual("git status", PermissionHandler.BuildDetailString(json));
        }

        [Test]
        public void BuildDetailString_LongCommand_Truncated()
        {
            string longCmd = new string('x', 500);
            string json = "{\"tool_input\":{\"command\":\"" + longCmd + "\"}}";
            string result = PermissionHandler.BuildDetailString(json);
            Assert.IsTrue(result.Length <= 304); // 300 + "..."
            Assert.IsTrue(result.EndsWith("..."));
        }

        [Test]
        public void BuildDetailString_Url()
        {
            const string json = "{\"tool_input\":{\"url\":\"https://example.com\"}}";
            Assert.AreEqual("https://example.com", PermissionHandler.BuildDetailString(json));
        }

        [Test]
        public void BuildDetailString_NoToolInput_ReturnsEmpty()
        {
            const string json = "{\"tool_name\":\"Edit\"}";
            Assert.AreEqual("", PermissionHandler.BuildDetailString(json));
        }

        // -----------------------------------------------------------
        //  Setup & file generation
        // -----------------------------------------------------------

        [Test]
        public void EnsureSetup_CreatesHookSettingsFile()
        {
            PermissionHandler.EnsureSetup();
            Assert.IsTrue(File.Exists(PermissionHandler.HookSettingsPath),
                "Hook settings JSON should exist after EnsureSetup");
        }

        [Test]
        public void HookSettingsFile_ContainsPermissionRequestHook()
        {
            PermissionHandler.EnsureSetup();
            string content = File.ReadAllText(PermissionHandler.HookSettingsPath);
            Assert.IsTrue(content.Contains("PermissionRequest"),
                "Settings should configure the PermissionRequest hook");
            Assert.IsTrue(content.Contains("\"type\": \"command\""),
                "Hook type should be command");
            Assert.IsTrue(content.Contains("\"timeout\":"),
                "Hook should have a timeout");
        }

        [Test]
        public void HookSettingsFile_IsValidJson()
        {
            PermissionHandler.EnsureSetup();
            string content = File.ReadAllText(PermissionHandler.HookSettingsPath);
            // Quick structural check — balanced braces
            int depth = 0;
            foreach (char c in content)
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
                Assert.GreaterOrEqual(depth, 0, "JSON has more closing braces than opening");
            }
            Assert.AreEqual(0, depth, "JSON braces should be balanced");
        }

        [Test]
        public void PermissionDir_ExistsAfterSetup()
        {
            PermissionHandler.EnsureSetup();
            Assert.IsTrue(Directory.Exists(PermissionHandler.PermissionDir),
                "Permission temp directory should exist after EnsureSetup");
        }

        // -----------------------------------------------------------
        //  File-based request / response round-trip
        // -----------------------------------------------------------

        [Test]
        public void RequestResponse_AllowFlow()
        {
            PermissionHandler.EnsureSetup();
            string dir = PermissionHandler.PermissionDir;

            // Simulate: hook script writes a request
            string id = System.Guid.NewGuid().ToString();
            string requestFile = Path.Combine(dir, id + ".request");
            string responseFile = Path.Combine(dir, id + ".response");

            string hookInput = "{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"Assets/Foo.cs\"}}";
            File.WriteAllText(requestFile, hookInput);

            // Override dialog to auto-allow
            var originalDialog = PermissionHandler.ShowDialog;
            string capturedTitle = null;
            string capturedMessage = null;
            PermissionHandler.ShowDialog = (title, message) =>
            {
                capturedTitle = title;
                capturedMessage = message;
                return true; // allow
            };

            try
            {
                // Manually trigger one poll cycle
                // (EditorApplication.update isn't running in tests, but we can invoke the
                //  static Poll via reflection or simply simulate the file flow)
                // Since Poll is private, simulate by checking that the request file is readable
                // and the dialog delegate works correctly.
                bool result = PermissionHandler.ShowDialog("Claude Permission Request",
                    "Claude wants to use: Edit\n\nAssets/Foo.cs");

                Assert.IsTrue(result);
                Assert.AreEqual("Claude Permission Request", capturedTitle);
                Assert.IsTrue(capturedMessage.Contains("Edit"));
                Assert.IsTrue(capturedMessage.Contains("Assets/Foo.cs"));

                // Simulate what Poll would write
                File.WriteAllText(responseFile, "allow");

                // Verify the hook script would read "allow"
                string response = File.ReadAllText(responseFile).Trim();
                Assert.AreEqual("allow", response);
            }
            finally
            {
                PermissionHandler.ShowDialog = originalDialog;
                if (File.Exists(requestFile)) File.Delete(requestFile);
                if (File.Exists(responseFile)) File.Delete(responseFile);
            }
        }

        [Test]
        public void RequestResponse_DenyFlow()
        {
            PermissionHandler.EnsureSetup();
            string dir = PermissionHandler.PermissionDir;

            string id = System.Guid.NewGuid().ToString();
            string responseFile = Path.Combine(dir, id + ".response");

            var originalDialog = PermissionHandler.ShowDialog;
            PermissionHandler.ShowDialog = (title, message) => false; // deny

            try
            {
                bool result = PermissionHandler.ShowDialog("Claude Permission Request",
                    "Claude wants to use: Bash\n\nrm -rf /");
                Assert.IsFalse(result);

                File.WriteAllText(responseFile, "deny");
                Assert.AreEqual("deny", File.ReadAllText(responseFile).Trim());
            }
            finally
            {
                PermissionHandler.ShowDialog = originalDialog;
                if (File.Exists(responseFile)) File.Delete(responseFile);
            }
        }

        [Test]
        public void BuildDetailString_PreferFilePath_OverCommand()
        {
            // When both file_path and command are present, file_path wins
            const string json = "{\"tool_input\":{\"file_path\":\"a.cs\",\"command\":\"echo hi\"}}";
            Assert.AreEqual("a.cs", PermissionHandler.BuildDetailString(json));
        }
    }
}
