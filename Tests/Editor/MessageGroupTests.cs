using NUnit.Framework;
using ClaudeCode.Editor.Rendering;

namespace ClaudeCode.Editor.Tests
{
    public class MessageGroupTests
    {
        // ── Constructor / plan mode class tests ──

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

        // ── ClassifyActions: Numbered options ──

        [Test]
        public void ClassifyActions_NumberedOptions_ReturnsItems()
        {
            var result = MessageGroup.ClassifyActions("Some context\n1. Foo\n2. Bar\n3. Baz");
            Assert.AreEqual(MessageGroup.ActionKind.NumberedOptions, result.Kind);
            Assert.AreEqual(3, result.Items.Count);
            Assert.AreEqual("Foo", result.Items[0]);
        }

        [Test]
        public void ClassifyActions_SingleNumberedItem_NoMatch()
        {
            var result = MessageGroup.ClassifyActions("Some context\n1. Only one");
            Assert.AreEqual(MessageGroup.ActionKind.None, result.Kind);
        }

        // ── ClassifyActions: Yes/No questions ──

        [Test]
        public void ClassifyActions_YesNo_WantMeTo()
        {
            var result = MessageGroup.ClassifyActions("Do you want me to proceed?");
            Assert.AreEqual(MessageGroup.ActionKind.YesNo, result.Kind);
        }

        [Test]
        public void ClassifyActions_YesNo_ShouldI()
        {
            var result = MessageGroup.ClassifyActions("Should I implement this now?");
            Assert.AreEqual(MessageGroup.ActionKind.YesNo, result.Kind);
        }

        [Test]
        public void ClassifyActions_YesNo_WouldYouLike()
        {
            var result = MessageGroup.ClassifyActions("Would you like me to start?");
            Assert.AreEqual(MessageGroup.ActionKind.YesNo, result.Kind);
        }

        [Test]
        public void ClassifyActions_YesNo_RequiresQuestionMark()
        {
            // "should i" without ? should NOT match YesNo
            var result = MessageGroup.ClassifyActions("I think should i do this.");
            Assert.AreNotEqual(MessageGroup.ActionKind.YesNo, result.Kind);
        }

        // ── ClassifyActions: Waiting for input ──

        [Test]
        public void ClassifyActions_WaitingForInput_Questions_Period()
        {
            var result = MessageGroup.ClassifyActions(
                "Before I finalize the plan, I have a couple of questions.");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_Questions_Colon()
        {
            var result = MessageGroup.ClassifyActions("Here are my questions:");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_WhatDoYouThink()
        {
            var result = MessageGroup.ClassifyActions("What do you think?");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_LetMeKnow()
        {
            var result = MessageGroup.ClassifyActions("Let me know your preference.");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_YourThoughts()
        {
            var result = MessageGroup.ClassifyActions("I'd love to hear your thoughts.");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_WhichApproach()
        {
            var result = MessageGroup.ClassifyActions("Which approach do you prefer?");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_PleaseClarify()
        {
            var result = MessageGroup.ClassifyActions("Could you please clarify the requirements?");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_GenericQuestionMark()
        {
            // Any response ending with ? should trigger WaitingForInput
            var result = MessageGroup.ClassifyActions("Which color do you want to pick?");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_QuestionOnSecondToLastLine()
        {
            // Question on second-to-last line, follow-up on last
            var result = MessageGroup.ClassifyActions(
                "Which color do you want to pick?\nWaiting on your pick!");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        [Test]
        public void ClassifyActions_WaitingForInput_Waiting()
        {
            var result = MessageGroup.ClassifyActions("Waiting on your pick!");
            Assert.AreEqual(MessageGroup.ActionKind.WaitingForInput, result.Kind);
        }

        // ── ClassifyActions: No match ──

        [Test]
        public void ClassifyActions_NoMatch_PlainStatement()
        {
            var result = MessageGroup.ClassifyActions("The code compiles successfully.");
            Assert.AreEqual(MessageGroup.ActionKind.None, result.Kind);
        }

        [Test]
        public void ClassifyActions_NoMatch_EmptyString()
        {
            var result = MessageGroup.ClassifyActions("");
            Assert.AreEqual(MessageGroup.ActionKind.None, result.Kind);
        }

        // ── ClassifyActions: Priority (numbered > yes/no > waiting) ──

        [Test]
        public void ClassifyActions_NumberedOptions_TakesPriority()
        {
            // Even if earlier text has questions, numbered options at end win
            var result = MessageGroup.ClassifyActions(
                "I have some questions.\n1. Option A\n2. Option B");
            Assert.AreEqual(MessageGroup.ActionKind.NumberedOptions, result.Kind);
        }

        [Test]
        public void ClassifyActions_NumberedOptions_FollowedByQuestion()
        {
            // Numbered list followed by "Which one?" — should detect the options, not WaitingForInput
            var result = MessageGroup.ClassifyActions(
                "Pick a color:\n1. Red\n2. Blue\n3. Green\n4. Purple\n\nWhich one?");
            Assert.AreEqual(MessageGroup.ActionKind.NumberedOptions, result.Kind);
            Assert.AreEqual(4, result.Items.Count);
            Assert.AreEqual("Red", result.Items[0]);
        }

        [Test]
        public void ClassifyActions_NumberedOptions_FollowedByTwoTrailingLines()
        {
            var result = MessageGroup.ClassifyActions(
                "Options:\n1. A\n2. B\n3. C\nLet me know.\nThanks!");
            Assert.AreEqual(MessageGroup.ActionKind.NumberedOptions, result.Kind);
            Assert.AreEqual(3, result.Items.Count);
        }
    }
}
