using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    public sealed class CapturePactTalentTests
    {
        private static CaptureMatchLogic Start(CaptureBoardSpec spec = null)
        {
            var match = new CaptureMatchLogic(new CaptureTuning(), spec ?? CaptureBoardSpec.V0100Sanctuary);
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsTrue(match.TryStart());
            return match;
        }

        private static void Tick(CaptureMatchLogic match, float seconds = 0.25f)
        {
            match.Tick(seconds, 1000f, 1000f, -1000f, -1000f);
        }

        [TestCase(179.5f, 180f, 1)]
        [TestCase(359.5f, 360f, 2)]
        [TestCase(539.5f, 540f, 3)]
        public void V11A01_TimeThreshold_UnlocksBothSidesOnSameTick(float before, float threshold, int tier)
        {
            var match = Start();
            match.SeedMatchElapsedForTest(before);
            Tick(match);
            Assert.Less(match.MatchElapsed, threshold);
            Assert.AreEqual(tier - 1, match.UnlockedTalentTier);

            Tick(match);
            Assert.AreEqual(threshold, match.MatchElapsed);
            Assert.AreEqual(tier, match.UnlockedTalentTier);
            Assert.AreEqual(1, match.PendingTalentTier(CaptureMatchLogic.BlueFactionId));
            Assert.AreEqual(1, match.PendingTalentTier(CaptureMatchLogic.RedFactionId));
        }

        [TestCase(249, 250, 1, true)]
        [TestCase(499, 500, 2, false)]
        [TestCase(749, 750, 3, true)]
        public void V11A02_EitherScore_UnlocksBothSidesOnSameTick(int before, int threshold, int tier, bool blueLeads)
        {
            var match = Start();
            match.SeedScoresForTest(blueLeads ? before : 0, blueLeads ? 0 : before);
            Tick(match);
            Assert.AreEqual(tier - 1, match.UnlockedTalentTier);
            match.SeedScoresForTest(blueLeads ? threshold : 0, blueLeads ? 0 : threshold);
            Tick(match);
            Assert.AreEqual(tier, match.UnlockedTalentTier);
            Assert.AreEqual(1, match.PendingTalentTier(CaptureMatchLogic.BlueFactionId));
            Assert.AreEqual(1, match.PendingTalentTier(CaptureMatchLogic.RedFactionId));
        }

        [Test]
        public void V11A02_ActualScoreTickCrosses250_BothSidesUnlockImmediately()
        {
            var match = Start();
            match.SeedScoresForTest(249, 0);
            Tick(match, 1f);
            Tick(match, 1f);
            Assert.AreEqual(249, match.BlueScore);
            Assert.AreEqual(0, match.UnlockedTalentTier);
            Tick(match, 1f);
            Assert.AreEqual(250, match.BlueScore);
            Assert.AreEqual(1, match.UnlockedTalentTier);
            Assert.AreEqual(1, match.PendingTalentTier(0));
            Assert.AreEqual(1, match.PendingTalentTier(1));
        }

        [Test]
        public void V11A03_MultiTierUnlock_ChoicesAreOrderedUniqueAndPerSide()
        {
            var match = Start();
            match.SeedScoresForTest(750, 0);
            Tick(match);
            Assert.AreEqual(3, match.UnlockedTalentTier);
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.TidalPull), "不能跳選 Tier 2");
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.None));
            Assert.IsFalse(match.TryChooseTalent(2, PactTalent.SwiftStep), "非法陣營");

            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.AreEqual(PactTalent.SwiftStep, match.SelectedTalent(0, 1));
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(1, 1));
            Assert.AreEqual(2, match.PendingTalentTier(0));
            Assert.AreEqual(1, match.PendingTalentTier(1));
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.StoneBody), "同階不可重選");
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.ExtremeOverclock), "不能從 Tier 1 跳選 Tier 3");

            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.TidalPull));
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.ExtremeOverclock));
            Assert.AreEqual(0, match.PendingTalentTier(0));
            Assert.AreEqual(1, match.PendingTalentTier(1));
            Assert.IsTrue(match.TryChooseTalent(1, PactTalent.FireSurge));
            Assert.AreEqual(PactTalent.FireSurge, match.SelectedTalent(1, 1));
            Assert.AreEqual(PactTalent.SwiftStep, match.SelectedTalent(0, 1));
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.ElementalAnnihilation));
        }

        [Test]
        public void V11A04_OnlyActiveCanChoose_SecondMatchClearsBothSides()
        {
            var match = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0100Sanctuary);
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.IsTrue(match.TryStart());
            match.SeedScoresForTest(250, 0);
            Tick(match);
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.IsTrue(match.TryChooseTalent(1, PactTalent.FireSurge));

            match.SeedScoresForTest(1000, 0);
            Tick(match, 1f);
            Assert.AreEqual(CaptureMatchState.Ended, match.State);
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.StoneBody));
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(0, 1));
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(1, 1));
            Tick(match, 3f);
            Assert.AreEqual(CaptureMatchState.Lobby, match.State);
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.StoneBody));
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(0, 1));
            Assert.IsTrue(match.TryExitCaptureMode());
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(0, 1));
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsTrue(match.TryStart());
            Assert.AreEqual(0, match.UnlockedTalentTier);
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(0, 1));
            Assert.AreEqual(PactTalent.None, match.SelectedTalent(1, 1));
            Assert.AreEqual(0, match.PendingTalentTier(0));
            Assert.AreEqual(0, match.PendingTalentTier(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void V11A05_LegacyCaptureSpecs_DoNotUnlockTalents(bool nineteen)
        {
            var match = Start(nineteen ? CaptureBoardSpec.V090Nineteen : CaptureBoardSpec.V080Seven);
            match.SeedMatchElapsedForTest(540f);
            match.SeedScoresForTest(750, 0);
            Tick(match);
            Assert.AreEqual(0, match.UnlockedTalentTier);
            Assert.AreEqual(0, match.PendingTalentTier(0));
            Assert.IsFalse(match.TryChooseTalent(0, PactTalent.SwiftStep));
        }
    }
}
