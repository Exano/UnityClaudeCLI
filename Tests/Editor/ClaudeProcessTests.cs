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
    }
}
