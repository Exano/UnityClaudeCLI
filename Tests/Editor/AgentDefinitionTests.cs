using System.IO;
using NUnit.Framework;
using ClaudeCode.Editor.Agents;

namespace ClaudeCode.Editor.Tests
{
    public class AgentDefinitionTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ClaudeCodeTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Test]
        public void Parse_WithFrontmatter_ExtractsNameAndKeywords()
        {
            var path = Path.Combine(_tempDir, "TestAgent.md");
            File.WriteAllText(path,
                "---\n" +
                "name: MyAgent\n" +
                "keywords: [foo, bar, baz]\n" +
                "---\n" +
                "# Instructions\nDo the thing.");

            var agent = AgentDefinition.Parse(path, true);

            Assert.AreEqual("MyAgent", agent.Name);
            Assert.AreEqual(new[] { "foo", "bar", "baz" }, agent.Keywords);
            Assert.IsTrue(agent.Content.Contains("# Instructions"));
            Assert.IsTrue(agent.Content.Contains("Do the thing."));
            Assert.IsTrue(agent.IsBuiltIn);
        }

        [Test]
        public void Parse_WithoutFrontmatter_UsesFilenameAsName()
        {
            var path = Path.Combine(_tempDir, "Programmer.md");
            File.WriteAllText(path, "# Programmer\nWrite clean code.");

            var agent = AgentDefinition.Parse(path, false);

            Assert.AreEqual("Programmer", agent.Name);
            Assert.AreEqual(0, agent.Keywords.Length);
            Assert.IsTrue(agent.Content.Contains("Write clean code."));
            Assert.IsFalse(agent.IsBuiltIn);
        }

        [Test]
        public void Parse_EmptyFrontmatter_DoesNotCrash()
        {
            var path = Path.Combine(_tempDir, "Empty.md");
            File.WriteAllText(path, "---\n---\nJust content.");

            var agent = AgentDefinition.Parse(path, true);

            Assert.AreEqual("Empty", agent.Name);
            Assert.AreEqual("Just content.", agent.Content);
        }

        [Test]
        public void Parse_MissingFile_ReturnsDefaultAgent()
        {
            var path = Path.Combine(_tempDir, "Missing.md");
            var agent = AgentDefinition.Parse(path, true);

            Assert.AreEqual("Missing", agent.Name);
            Assert.AreEqual("", agent.Content);
        }

        [Test]
        public void ScorePrompt_MatchesKeywords()
        {
            var agent = new AgentDefinition
            {
                Name = "UI",
                Keywords = new[] { "button", "canvas", "panel" }
            };

            Assert.AreEqual(2, agent.ScorePrompt("Add a button to the panel"));
            Assert.AreEqual(0, agent.ScorePrompt("Fix the null reference in PlayerController"));
            Assert.AreEqual(1, agent.ScorePrompt("Create a CANVAS for the HUD"));
        }

        [Test]
        public void ScorePrompt_CaseInsensitive()
        {
            var agent = new AgentDefinition
            {
                Name = "Shader",
                Keywords = new[] { "shader", "URP" }
            };

            Assert.AreEqual(1, agent.ScorePrompt("Write a SHADER"));
            Assert.AreEqual(2, agent.ScorePrompt("URP shader pass"));
        }

        [Test]
        public void ScorePrompt_ShortKeyword_UsesWordBoundary()
        {
            var agent = new AgentDefinition
            {
                Name = "UI",
                Keywords = new[] { "UI" }
            };

            // "UI" as a standalone word should match
            Assert.AreEqual(1, agent.ScorePrompt("Build a UI panel"));
            // "UI" inside another word should NOT match
            Assert.AreEqual(0, agent.ScorePrompt("Build a GUI system"));
            Assert.AreEqual(0, agent.ScorePrompt("require something"));
        }

        [Test]
        public void ScorePrompt_NoKeywords_ReturnsZero()
        {
            var agent = new AgentDefinition
            {
                Name = "Base",
                Keywords = new string[0]
            };

            Assert.AreEqual(0, agent.ScorePrompt("anything at all"));
        }

        [Test]
        public void Parse_KeywordsWithQuotes_StripsQuotes()
        {
            var path = Path.Combine(_tempDir, "Quoted.md");
            File.WriteAllText(path,
                "---\n" +
                "name: Quoted\n" +
                "keywords: [\"hello\", 'world']\n" +
                "---\n" +
                "Content.");

            var agent = AgentDefinition.Parse(path, true);

            Assert.AreEqual(new[] { "hello", "world" }, agent.Keywords);
        }
    }
}
