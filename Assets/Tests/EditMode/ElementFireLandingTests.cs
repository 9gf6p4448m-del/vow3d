using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // PHASE2_BATCH4_PLAN.md §8 末段點名的兩個無測試分支（§4-9 的火落點細則）。
    // 步驟 A 的實作者把它們寫出來了，但沒有任何測試守——這兩條補上。
    public sealed class ElementFireLandingTests
    {
        private static readonly ElementTuning Tuning = new ElementTuning();

        private static int CountOfKind(ElementZoneField field, ElementZoneKind kind)
        {
            int n = 0;
            for (int slot = 0; slot < field.Capacity; slot++)
                if (field.TryGetBySlot(slot, out ElementZone zone) && zone.Kind == kind) n++;
            return n;
        }

        // 火落在**既有燃燒區**內 → 視同空地，但不新增第二個燃燒區：效果等同把剩餘時間刷新回 4s，
        // 圓心留在舊的那個。壞實作＝疊區，同一塊地變成每秒 40 傷（DPS 直接翻倍）。
        [Test]
        public void Fire_LandingInsideAnExistingBurningZone_RefreshesItInPlace_AndDoesNotStackASecondZone()
        {
            ElementZoneField field = new ElementZoneField(Tuning);
            int oldId = field.Spawn(ElementZoneKind.Burning, 2f, 3f, Tuning.BurnRadius, Tuning.BurnDurationSeconds, 1);
            field.Tick(1.5f);
            Assert.IsTrue(field.TryGetById(oldId, out ElementZone before));
            Assert.AreEqual(2.5f, before.RemainingSeconds, 1e-4f, "前提：舊燃燒區已經燒掉 1.5s");

            // 落點離舊圓心 1.17m（在 3m 圈內），刻意避開軸對齊
            ReactionOutcome outcome = ElementReactionLogic.Resolve(
                ElementCast.Fire, 1, 3.1f, 3.4f, 0f, 0f, field, Tuning);

            Assert.AreEqual(ElementReaction.PlainFire, outcome.Reaction, "落在燃燒區內應該視同空地火");
            Assert.AreEqual(Tuning.FireDirectDamage, outcome.AoeDamage, 1e-4f);
            Assert.AreEqual(oldId, outcome.ConsumedZoneId, "舊燃燒區應該被就地換掉，不是另開一個");
            Assert.AreEqual(1, CountOfKind(field, ElementZoneKind.Burning), "同一塊地疊出了第二個燃燒區");
            Assert.IsFalse(field.TryGetById(oldId, out ElementZone _), "舊燃燒區還在");

            int refreshedId = field.FindNearestContaining(3.1f, 3.4f, ElementZoneKind.Burning);
            Assert.GreaterOrEqual(refreshedId, 0, "刷新之後找不到燃燒區");
            Assert.IsTrue(field.TryGetById(refreshedId, out ElementZone after));
            Assert.AreEqual(Tuning.BurnDurationSeconds, after.RemainingSeconds, 1e-4f, "剩餘時間沒有刷新回 4s");
            Assert.AreEqual(2f, after.X, 1e-4f, "圓心跑掉了（應該留在舊燃燒區的圓心）");
            Assert.AreEqual(3f, after.Z, 1e-4f, "圓心跑掉了（應該留在舊燃燒區的圓心）");
            Assert.IsFalse(outcome.IsCombo, "空地火不是 Combo");
        }

        // 火落在**蒸氣**內 → 視同空地（蒸氣不消耗、不參與反應）。
        // 壞實作＝把蒸氣當成可反應的區域，白霧會被自己的火吃掉。
        [Test]
        public void Fire_LandingInsideSteam_IsTreatedAsOpenGround_AndDoesNotConsumeTheSteam()
        {
            ElementZoneField field = new ElementZoneField(Tuning);
            int steamId = field.Spawn(ElementZoneKind.Steam, 5f, 5f, Tuning.ReactionRadius,
                                      Tuning.SteamDurationSeconds, ElementReactionLogic.NeutralFactionId);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(
                ElementCast.Fire, 1, 5.9f, 5.4f, 0f, 0f, field, Tuning);

            Assert.AreEqual(ElementReaction.PlainFire, outcome.Reaction, "落在蒸氣內應該視同空地火");
            Assert.AreEqual(Tuning.FireDirectDamage, outcome.AoeDamage, 1e-4f);
            Assert.AreEqual(-1, outcome.ConsumedZoneId, "空地火不該消耗任何區域");
            Assert.IsTrue(field.TryGetById(steamId, out ElementZone steam), "蒸氣被火吃掉了");
            Assert.AreEqual(Tuning.SteamDurationSeconds, steam.RemainingSeconds, 1e-4f, "蒸氣的壽命被動過");

            Assert.AreEqual(1, CountOfKind(field, ElementZoneKind.Burning), "沒有留下燃燒區");
            int burningId = field.FindNearestContaining(5.9f, 5.4f, ElementZoneKind.Burning);
            Assert.IsTrue(field.TryGetById(burningId, out ElementZone burning));
            Assert.AreEqual(5.9f, burning.X, 1e-4f, "燃燒區應該生在火的落點");
            Assert.AreEqual(5.4f, burning.Z, 1e-4f, "燃燒區應該生在火的落點");
            Assert.AreEqual(1, CountOfKind(field, ElementZoneKind.Steam), "蒸氣的數量變了");
        }
    }
}
