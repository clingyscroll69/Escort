using HS.Tutorial;
using NUnit.Framework;

/// <summary>
/// The whole PlayMode run (a SetUpFixture outside any namespace covers the assembly) keeps the tutorial on a throwaway
/// store. The editor and its test runs share PlayerPrefs, so a player flow in a test (the opening test clicks through
/// as a player) would otherwise mark lessons seen for whoever presses Play next. Tests that need a clean slate still
/// swap in their own MemoryStore.
/// </summary>
[SetUpFixture]
public class TutorialStoreFixture
{
    ITutorialStore _saved;

    [OneTimeSetUp]
    public void UseThrowawayStore()
    {
        _saved = TutorialProgress.Store;
        TutorialProgress.Store = new MemoryStore();
    }

    [OneTimeTearDown]
    public void RestoreStore() => TutorialProgress.Store = _saved;
}

namespace HS.Tests
{
    public class TutorialStoreIsolationTests
    {
        [Test]
        public void Tests_Never_Touch_The_Saved_Tutorial()
        {
            Assert.IsInstanceOf<MemoryStore>(TutorialProgress.Store, "PlayMode tests run on a throwaway tutorial store");
        }
    }
}
