using NUnit.Framework;
using ClaudeCode.Editor.Rendering;

namespace ClaudeCode.Editor.Tests
{
    public class MessageGroupTests
    {
        [Test]
        public void Constructor_PlanMode_AddsPlanClass()
        {
            var group = new MessageGroup(isPlanMode: true);
            Assert.IsTrue(group.ClassListContains("message-group--plan"));
        }

        [Test]
        public void Constructor_NormalMode_NoPlanClass()
        {
            var group = new MessageGroup(isPlanMode: false);
            Assert.IsFalse(group.ClassListContains("message-group--plan"));
        }

        [Test]
        public void Constructor_Default_NoPlanClass()
        {
            var group = new MessageGroup();
            Assert.IsFalse(group.ClassListContains("message-group--plan"));
        }

        [Test]
        public void Constructor_PlanMode_HasMessageGroupClass()
        {
            var group = new MessageGroup(isPlanMode: true);
            Assert.IsTrue(group.ClassListContains("message-group"));
        }
    }
}
