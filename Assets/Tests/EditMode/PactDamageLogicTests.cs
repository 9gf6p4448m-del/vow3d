using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    public sealed class PactDamageLogicTests
    {
        private static CaptureMatchLogic Start(CaptureBoardSpec spec = null)
        {
            var match = new CaptureMatchLogic(new CaptureTuning(), spec ?? CaptureBoardSpec.V0100Sanctuary);
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsTrue(match.TryStart());
            return match;
        }

        private static void UnlockThirdTier(CaptureMatchLogic match)
        {
            match.SeedScoresForTest(750, 0);
            match.Tick(1f, 1000f, 1000f, -1000f, -1000f);
            Assert.AreEqual(3, match.UnlockedTalentTier);
        }

        [Test]
        public void B301_GeoFrenzyReadsCurrentPositionAndOwnershipOnEveryDamage()
        {
            var match = Start();
            int tile = match.Spec.MotherTile(CaptureMatchLogic.BlueFactionId, 0);
            float x = match.Spec.CenterX(tile);
            float z = match.Spec.CenterZ(tile);
            Assert.AreEqual(1f, match.DamageMultiplierFor(0, x, z));

            UnlockThirdTier(match);
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.TidalPull));
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.GeothermalFrenzy));
            Assert.AreEqual(1.15f, match.DamageMultiplierFor(0, x, z), 1e-5f);
            Assert.AreEqual(1f, match.DamageMultiplierFor(0, 1000f, 1000f));
            Assert.AreEqual(1f, match.DamageMultiplierFor(1, x, z));

            int[] owners = new int[match.TileCount];
            for (int i = 0; i < owners.Length; i++) owners[i] = match.OwnerOf(i);
            owners[tile] = CaptureMatchLogic.RedFactionId;
            match.SeedOwnershipForTest(owners);
            Assert.AreEqual(1f, match.DamageMultiplierFor(0, x, z));

            owners[tile] = CaptureMatchLogic.BlueFactionId;
            match.SeedOwnershipForTest(owners);
            Assert.AreEqual(1.15f, match.DamageMultiplierFor(0, x, z), 1e-5f);
            match.SeedScoresForTest(1000, 0);
            match.Tick(1f, 1000f, 1000f, -1000f, -1000f);
            Assert.AreEqual(CaptureMatchState.Ended, match.State);
            Assert.AreEqual(1f, match.DamageMultiplierFor(0, x, z));
        }

        [Test]
        public void B304_LegacyRulesAndNewMatchDoNotKeepGeoFrenzy()
        {
            var legacy = Start(CaptureBoardSpec.V090Nineteen);
            Assert.AreEqual(1f, legacy.DamageMultiplierFor(0, 0f, 0f));

            var match = Start();
            UnlockThirdTier(match);
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.TidalPull));
            Assert.IsTrue(match.TryChooseTalent(0, PactTalent.GeothermalFrenzy));
            int tile = match.Spec.MotherTile(0, 0);
            Assert.AreEqual(1.15f, match.DamageMultiplierFor(0, match.Spec.CenterX(tile), match.Spec.CenterZ(tile)), 1e-5f);

            match.SeedScoresForTest(1000, 0);
            match.Tick(1f, 1000f, 1000f, -1000f, -1000f);
            match.Tick(3f, 1000f, 1000f, -1000f, -1000f);
            Assert.IsTrue(match.TryStart());
            Assert.AreEqual(1f, match.DamageMultiplierFor(0, match.Spec.CenterX(tile), match.Spec.CenterZ(tile)));
        }
    }
}
