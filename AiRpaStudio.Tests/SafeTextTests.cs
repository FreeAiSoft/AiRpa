using NUnit.Framework;
using RPA.Chat.Services;

namespace RPA.Chat.Tests
{
    [TestFixture]
    public class SafeTextTests
    {
        [Test]
        public void Truncate_ShortText_IsUnchanged()
        {
            Assert.That(SafeText.Truncate("abc", 5), Is.EqualTo("abc"));
        }

        [Test]
        public void Truncate_DoesNotSplitSurrogatePair()
        {
            Assert.That(SafeText.Truncate("a\uD83D\uDE00b", 2), Is.EqualTo("a"));
        }

        [Test]
        public void Truncate_KeepsCompleteEmojiWhenItFits()
        {
            Assert.That(SafeText.Truncate("\uD83D\uDE00ab", 2), Is.EqualTo("\uD83D\uDE00"));
        }

        [Test]
        public void Tail_DoesNotStartWithUnpairedLowSurrogate()
        {
            Assert.That(SafeText.Tail("a\uD83D\uDE00b", 2), Is.EqualTo("b"));
        }
    }
}
