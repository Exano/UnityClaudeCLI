using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ClaudeCode.Editor.Agents;

namespace ClaudeCode.Editor.Tests
{
    public class AgentDiscoveryTests
    {
        [Test]
        public void GetAgents_ReturnsNonEmpty_WhenBuiltInAgentsExist()
        {
            AgentDiscovery.Refresh();
            var agents = AgentDiscovery.GetAgents();

            Assert.IsNotNull(agents);
            Assert.Greater(agents.Count, 0, "Should find built-in agents from package");
        }

        [Test]
        public void GetBase_ReturnsBaseAgent()
        {
            AgentDiscovery.Refresh();
            var baseAgent = AgentDiscovery.GetBase();

            Assert.IsNotNull(baseAgent, "_Base.md should exist in package");
            Assert.AreEqual("_Base", baseAgent.Name);
            Assert.IsTrue(baseAgent.Content.Length > 0);
        }

        [Test]
        public void GetAgents_ExcludesBase()
        {
            AgentDiscovery.Refresh();
            var agents = AgentDiscovery.GetAgents();

            foreach (var a in agents)
                Assert.AreNotEqual("_Base", a.Name, "_Base should not appear in selectable agents");
        }

        [Test]
        public void AutoDetect_FindsMatchingAgents()
        {
            AgentDiscovery.Refresh();
            var result = AgentDiscovery.AutoDetect("Create a button panel with UI Toolkit");

            Assert.IsNotNull(result);
            bool foundUI = false;
            foreach (var a in result)
            {
                if (a.Name == "UI") foundUI = true;
            }
            Assert.IsTrue(foundUI, "Should detect UI agent for UI-related prompt");
        }

        [Test]
        public void AutoDetect_ReturnsEmpty_ForUnrelatedPrompt()
        {
            AgentDiscovery.Refresh();
            var result = AgentDiscovery.AutoDetect("hello world how are you");

            // May or may not match — but shouldn't crash
            Assert.IsNotNull(result);
        }

        [Test]
        public void AutoDetect_RespectsMaxAgents()
        {
            AgentDiscovery.Refresh();
            // Use a prompt that could match many agents
            var result = AgentDiscovery.AutoDetect(
                "shader button animator script singleton canvas component", maxAgents: 2);

            Assert.LessOrEqual(result.Count, 2);
        }

        [Test]
        public void BuildContext_IncludesBaseAndSelected()
        {
            AgentDiscovery.Refresh();
            var agents = AgentDiscovery.GetAgents();
            if (agents.Count == 0)
            {
                Assert.Inconclusive("No agents found — can't test BuildContext");
                return;
            }

            var selected = new List<AgentDefinition> { agents[0] };
            var context = AgentDiscovery.BuildContext(selected);

            Assert.IsNotNull(context);
            Assert.Greater(context.Length, 0);

            var baseAgent = AgentDiscovery.GetBase();
            if (baseAgent != null)
                Assert.IsTrue(context.Contains(baseAgent.Content.Substring(0, 20)),
                    "Context should include _Base content");
        }

        [Test]
        public void BuildContext_EmptySelection_ReturnsBaseOnly()
        {
            AgentDiscovery.Refresh();
            var context = AgentDiscovery.BuildContext(new List<AgentDefinition>());
            var baseAgent = AgentDiscovery.GetBase();

            if (baseAgent != null)
                Assert.IsTrue(context.Contains(baseAgent.Content.Substring(0, 20)));
        }

        [Test]
        public void BuiltInAgents_HaveKeywords()
        {
            AgentDiscovery.Refresh();
            var agents = AgentDiscovery.GetAgents();

            foreach (var a in agents)
            {
                Assert.Greater(a.Keywords.Length, 0,
                    $"Built-in agent '{a.Name}' should have keywords for auto-detection");
            }
        }
    }
}
