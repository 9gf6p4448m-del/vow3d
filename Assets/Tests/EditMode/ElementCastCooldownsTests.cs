using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-ac～V2-ad：三技能各自獨立 5s 冷卻＋HUD 標籤索引。
    public sealed class ElementCastCooldownsTests
    {
        // V2-ac Cast_IsRejectedDuringTheFiveSecondCooldown_AndAcceptedAtFiveSeconds
        [Test]
        public void Cast_IsRejectedDuringTheFiveSecondCooldown_AndAcceptedAtFiveSeconds()
        {
            ElementTuning tuning = new ElementTuning();
            ElementCastCooldowns cooldowns = new ElementCastCooldowns(tuning);

            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Fire, 0f));
            Assert.IsFalse(cooldowns.TryBeginCast(ElementCast.Fire, 4.99f), "冷卻中必須拒收");
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Fire, 5.0f), "滿 5s 必須可再放");

            // 三技能各自獨立：Fire 在冷卻中，Water 完全不受影響
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Water, 4.99f));
        }

        // V2-ad RemainingLabelIndex_CountsDownFiveToOne_AndReturnsZeroWhenReady
        [Test]
        public void RemainingLabelIndex_CountsDownFiveToOne_AndReturnsZeroWhenReady()
        {
            ElementTuning tuning = new ElementTuning();
            ElementCastCooldowns cooldowns = new ElementCastCooldowns(tuning);

            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Fire, 0f));

            Assert.AreEqual(5, cooldowns.RemainingLabelIndex(ElementCast.Fire, 0.01f));
            Assert.AreEqual(1, cooldowns.RemainingLabelIndex(ElementCast.Fire, 4.01f));
            Assert.AreEqual(1, cooldowns.RemainingLabelIndex(ElementCast.Fire, 4.99f));
            Assert.AreEqual(0, cooldowns.RemainingLabelIndex(ElementCast.Fire, 5.0f));
        }
    }
}
