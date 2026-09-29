using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    public sealed class CaptureVisibilityLogicTests
    {
        private static CaptureMatchLogic NewActive()
        {
            var match = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0100Sanctuary);
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsTrue(match.TryStart());
            return match;
        }

        [Test]
        public void LocalVision_IncludesExactlySixMeters_ForBothSides()
        {
            var match = NewActive();
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 106f, 100f));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 1, 100f, 100f, false, 106f, 100f));
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 106.001f, 100f));
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 1, 100f, 100f, false, 106.001f, 100f));
        }

        [Test]
        public void OwnedTile_RevealsRemotely_EnemyTileDoesNot()
        {
            var match = NewActive();
            Assert.AreEqual(0, match.OwnerOf(13));
            Assert.AreEqual(1, match.OwnerOf(7));
            Assert.IsTrue(CaptureVisibilityLogic.HasTrueVision(match, 0, 0f, -15.15625f));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, -15.15625f));
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 15.15625f));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 1, 100f, 100f, false, 0f, 15.15625f));
            Assert.IsFalse(CaptureVisibilityLogic.HasTrueVision(match, 0, 100f, 100f));
        }

        [Test]
        public void KnockedOutHero_LosesLocalVision_ButOwnedTileStillReveals()
        {
            var match = NewActive();
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, true, 101f, 100f));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, true, 0f, -15.15625f));
        }

        [Test]
        public void OwnershipChanges_AreReadImmediatelyWithoutRevealCache()
        {
            var match = NewActive();
            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            owners[0] = CaptureMatchLogic.BlueFactionId;
            match.SeedOwnershipForTest(owners);
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 0f));
            owners[0] = CaptureMatchLogic.NeutralFactionId;
            match.SeedOwnershipForTest(owners);
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 0f));
        }

        [Test]
        public void EncircleCut_RemovesTrueVisionOnTheFlipTick()
        {
            var match = NewActive();
            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            foreach (int tile in new[] { 12, 13, 14, 4, 0 }) owners[tile] = CaptureMatchLogic.BlueFactionId;
            foreach (int tile in new[] { 7, 8, 18, 1, 2, 3 }) owners[tile] = CaptureMatchLogic.RedFactionId;
            match.SeedOwnershipForTest(owners);
            for (int tick = 1; tick <= 15; tick++)
                match.Tick(0.25f, 1000f, 1000f, tick <= 2 ? 1000f : 0f,
                    tick <= 2 ? 1000f : -7.578125f);
            Assert.AreEqual(CaptureMatchLogic.BlueFactionId, match.OwnerOf(0));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 0f));

            match.Tick(0.25f, 1000f, 1000f, 0f, -7.578125f);
            Assert.AreEqual(CaptureMatchLogic.RedFactionId, match.OwnerOf(4), "紅方須真的翻下連接塊");
            Assert.AreEqual(CaptureMatchLogic.NeutralFactionId, match.OwnerOf(0), "0 號當 tick 斷能");
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 0f),
                "斷能當 tick 不得殘留真視野");
        }

        [Test]
        public void LegacyRulesAndInactiveState_KeepTargetsVisible()
        {
            var match = NewActive();
            var legacy = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsFalse(CaptureVisibilityLogic.AppliesTo(legacy));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(legacy, 0, 100f, 100f, true, 0f, 0f));
            Assert.IsFalse(CaptureVisibilityLogic.AppliesTo(new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0100Sanctuary)));
            Assert.IsTrue(CaptureVisibilityLogic.AppliesTo(match));
        }

        [Test]
        public void SecondRound_DoesNotKeepPreviousTileVision()
        {
            var match = NewActive();
            int[] owners = new int[19];
            for (int i = 0; i < owners.Length; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            owners[0] = CaptureMatchLogic.BlueFactionId;
            match.SeedOwnershipForTest(owners);
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 0f));

            match.SeedMatchElapsedForTest(899.9f);
            match.Tick(0.2f, 100f, 100f, 100f, 100f);
            Assert.AreEqual(CaptureMatchState.Ended, match.State);
            match.Tick(3.1f, 100f, 100f, 100f, 100f);
            Assert.AreEqual(CaptureMatchState.Lobby, match.State);
            Assert.IsTrue(match.TryStart());
            Assert.AreEqual(CaptureMatchLogic.NeutralFactionId, match.OwnerOf(0));
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, 0f));
            Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, 0, 100f, 100f, false, 0f, -15.15625f));
        }
    }
}
