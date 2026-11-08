using NUnit.Framework;

namespace Words.UiMockups.WordListHub.Tests
{
    public sealed class MockupWordListEditSessionTests
    {
        [Test]
        public void AddAtFrontSelectsAnEmptyDraftAndKeepsCanonicalWordsInOrder()
        {
            const string chineseWord = "苹果 | ping guo";
            var session = new MockupWordListEditSession(new[] { chineseWord, "garden" });

            session.AddAtFront();

            Assert.That(session.SelectedIndex, Is.EqualTo(0));
            Assert.That(session.SelectedWord, Is.Empty);
            CollectionAssert.AreEqual(new[] { "", chineseWord, "garden" }, session.ToList());
        }

        [Test]
        public void SelectedWordPreservesCanonicalPinyinAndWhitespaceExactly()
        {
            const string canonicalWord = "苹果 | ping guo";
            var session = new MockupWordListEditSession(new[] { canonicalWord, "garden" });
            session.Select(1);
            session.SetSelectedWord("banana | ba na na");

            CollectionAssert.AreEqual(new[] { canonicalWord, "banana | ba na na" }, session.ToList());
        }

        [Test]
        public void MovingChangesOrderAndReselectsTheMovedWord()
        {
            const string canonicalWord = "苹果 | ping guo";
            var session = new MockupWordListEditSession(new[] { "first", canonicalWord, "third" });
            session.Select(1);

            Assert.That(session.MoveSelected(-1), Is.True);

            CollectionAssert.AreEqual(new[] { canonicalWord, "first", "third" }, session.ToList());
            Assert.That(session.SelectedIndex, Is.EqualTo(0));
            Assert.That(session.SelectedWord, Is.EqualTo(canonicalWord));
        }

        [Test]
        public void MoveAtEitherBoundaryLeavesOrderAndSelectionUnchanged()
        {
            var session = new MockupWordListEditSession(new[] { "first", "last" });
            Assert.That(session.MoveSelected(-1), Is.False);
            Assert.That(session.SelectedIndex, Is.EqualTo(0));

            session.Select(1);
            Assert.That(session.MoveSelected(1), Is.False);
            CollectionAssert.AreEqual(new[] { "first", "last" }, session.ToList());
            Assert.That(session.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public void DeleteSelectsTheNearestRemainingWordAndClearsSelectionWhenEmpty()
        {
            var session = new MockupWordListEditSession(new[] { "first", "middle", "last" });
            session.Select(1);

            Assert.That(session.DeleteSelected(), Is.True);
            CollectionAssert.AreEqual(new[] { "first", "last" }, session.ToList());
            Assert.That(session.SelectedIndex, Is.EqualTo(1));
            Assert.That(session.SelectedWord, Is.EqualTo("last"));

            Assert.That(session.DeleteSelected(), Is.True);
            Assert.That(session.SelectedIndex, Is.EqualTo(0));
            Assert.That(session.SelectedWord, Is.EqualTo("first"));
            Assert.That(session.DeleteSelected(), Is.True);
            Assert.That(session.HasSelection, Is.False);
            Assert.That(session.SelectedIndex, Is.EqualTo(-1));
            Assert.That(session.DeleteSelected(), Is.False);
        }
    }
}
