using NUnit.Framework;
using ClaudeCode.Editor;

namespace ClaudeCode.Editor.Tests
{
    public class ClaudeProcessTests
    {
        [Test]
        public void BuildFlags_AutoApprove_ContainsDangerouslySkip()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.AutoApprove, null, 0, false, null, null);
            Assert.IsTrue(flags.Contains("--dangerously-skip-permissions"));
        }

        [Test]
        public void BuildFlags_Plan_ContainsPermissionModePlan()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.Plan, null, 0, false, null, null);
            Assert.IsTrue(flags.Contains("--permission-mode plan"));
        }

        [Test]
        public void BuildFlags_Plan_DoesNotContainDangerouslySkip()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.Plan, null, 0, false, null, null);
            Assert.IsFalse(flags.Contains("--dangerously-skip-permissions"));
        }

        [Test]
        public void BuildFlags_Default_NoSettingsFile_NoPermissionFlag()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.Default, null, 0, false, null, null);
            Assert.IsFalse(flags.Contains("--dangerously-skip-permissions"));
            Assert.IsFalse(flags.Contains("--permission-mode"));
            Assert.IsFalse(flags.Contains("--settings"));
        }

        [Test]
        public void BuildFlags_WithModel_ContainsModelFlag()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.AutoApprove, "claude-sonnet-4-6", 0, false, null, null);
            Assert.IsTrue(flags.Contains("--model claude-sonnet-4-6"));
        }

        [Test]
        public void BuildFlags_WithMaxTurns_ContainsMaxTurnsFlag()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.AutoApprove, null, 5, false, null, null);
            Assert.IsTrue(flags.Contains("--max-turns 5"));
        }

        [Test]
        public void BuildFlags_ZeroMaxTurns_OmitsMaxTurnsFlag()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.AutoApprove, null, 0, false, null, null);
            Assert.IsFalse(flags.Contains("--max-turns"));
        }

        [Test]
        public void BuildFlags_Resume_WithValidSession_ContainsContinue()
        {
            var sessionId = "12345678-1234-1234-1234-123456789abc";
            var flags = ClaudeProcess.BuildFlags(PermissionMode.AutoApprove, null, 0, true, sessionId, null);
            Assert.IsTrue(flags.Contains($"--continue {sessionId}"));
        }

        [Test]
        public void BuildFlags_Resume_WithInvalidSession_OmitsContinue()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.AutoApprove, null, 0, true, "not-a-guid", null);
            Assert.IsFalse(flags.Contains("--continue"));
        }

        [Test]
        public void BuildFlags_AlwaysContainsStreamJson()
        {
            var flags = ClaudeProcess.BuildFlags(PermissionMode.Plan, null, 0, false, null, null);
            Assert.IsTrue(flags.Contains("--output-format stream-json"));
        }
        // ── ExtractToolInputQuestion ──

        [Test]
        public void ExtractToolInputQuestion_BasicQuestion()
        {
            const string json = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\",\"input\":{\"question\":\"Which color do you prefer?\"}}]}}";
            Assert.AreEqual("Which color do you prefer?",
                ClaudeProcess.ExtractToolInputQuestion(json));
        }

        [Test]
        public void ExtractToolInputQuestion_WithNewlines()
        {
            const string json = "{\"input\":{\"question\":\"Pick one:\\n1. Red\\n2. Blue\\n3. Green\"}}";
            var result = ClaudeProcess.ExtractToolInputQuestion(json);
            Assert.IsTrue(result.Contains("1. Red"));
            Assert.IsTrue(result.Contains("\n"));
        }

        [Test]
        public void ExtractToolInputQuestion_WithEscapedQuotes()
        {
            const string json = "{\"input\":{\"question\":\"Do you want the \\\"dark\\\" theme?\"}}";
            Assert.AreEqual("Do you want the \"dark\" theme?",
                ClaudeProcess.ExtractToolInputQuestion(json));
        }

        [Test]
        public void ExtractToolInputQuestion_NoQuestionField_ReturnsNull()
        {
            const string json = "{\"input\":{\"prompt\":\"Hello\"}}";
            Assert.IsNull(ClaudeProcess.ExtractToolInputQuestion(json));
        }

        [Test]
        public void ExtractToolInputQuestion_NumberedOptions_Preserved()
        {
            const string json = "{\"input\":{\"question\":\"Which approach?\\n1. Option A\\n2. Option B\\n3. Option C\"}}";
            var result = ClaudeProcess.ExtractToolInputQuestion(json);
            Assert.IsTrue(result.Contains("1. Option A"));
            Assert.IsTrue(result.Contains("2. Option B"));
            Assert.IsTrue(result.Contains("3. Option C"));
        }
    }
}
