using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-y～V2-ab：蒸氣迷霧的遮蔽真值表、受擊顯影計時、引導係數。
    public sealed class SteamConcealmentLogicTests
    {
        private const float Epsilon = 1e-4f;

        // V2-y TargetInsideSteam_IsConcealed_FromAnAttackerOutside_ButNotFromOneInTheSameSteam
        [Test]
        public void TargetInsideSteam_IsConcealed_FromAnAttackerOutside_ButNotFromOneInTheSameSteam()
        {
            Assert.IsTrue(SteamConcealmentLogic.IsConcealed(true, false, false),
                "在霧內、攻擊者在霧外、沒顯影 → 必須遮蔽");
            Assert.IsFalse(SteamConcealmentLogic.IsConcealed(true, true, false),
                "攻擊者跟目標同一團霧 → 不得遮蔽（霧裡自己人打得到）");
            Assert.IsFalse(SteamConcealmentLogic.IsConcealed(false, false, false),
                "目標根本不在霧內 → 不得遮蔽");
        }

        // V2-z AtOnePointFourNineSeconds_StillRevealed_AtOnePointFiveOne_ConcealedAgain
        [Test]
        public void AtOnePointFourNineSeconds_StillRevealed_AtOnePointFiveOne_ConcealedAgain()
        {
            ElementTuning tuning = new ElementTuning();
            SteamConcealmentLogic logic = new SteamConcealmentLogic(tuning);

            logic.NotifyDamaged();
            logic.Tick(1.49f);
            Assert.IsTrue(logic.IsRevealed, "累計 1.49s（< 1.5s）仍應顯影中");
            Assert.IsFalse(SteamConcealmentLogic.IsConcealed(true, false, logic.IsRevealed),
                "顯影中即使在霧內、攻擊者在霧外，也不得遮蔽");

            logic.Tick(0.02f); // 累計 1.51s
            Assert.IsFalse(logic.IsRevealed, "累計 1.51s（> 1.5s）顯影必須結束");
            Assert.IsTrue(SteamConcealmentLogic.IsConcealed(true, false, logic.IsRevealed),
                "顯影結束後恢復遮蔽");
        }

        // V2-aa BeingDamagedAgain_RefreshesTheRevealToOnePointFive
        [Test]
        public void BeingDamagedAgain_RefreshesTheRevealToOnePointFive()
        {
            ElementTuning tuning = new ElementTuning();
            SteamConcealmentLogic logic = new SteamConcealmentLogic(tuning);

            logic.NotifyDamaged();
            logic.Tick(1.2f); // 剩 0.3s
            Assert.AreEqual(0.3f, logic.RevealRemainingSeconds, Epsilon);

            logic.NotifyDamaged();
            Assert.AreEqual(1.5f, logic.RevealRemainingSeconds, Epsilon,
                "重複受傷必須刷新回 1.5，不得疊加成 1.8");
        }

        // V2-ab ChannelSpeedMultiplier_IsHalvedInsideSteam_AndFullOutside
        [Test]
        public void ChannelSpeedMultiplier_IsHalvedInsideSteam_AndFullOutside()
        {
            ElementTuning tuning = new ElementTuning();

            // 期望值刻意寫死 0.5（不讀 tuning.SteamChannelSpeedMultiplier）：兩邊讀同一個欄位，
            // 數值被突變時期望值會跟著漂移，測試永遠綠燈、抓不到 E5。
            Assert.AreEqual(0.5f, SteamConcealmentLogic.ChannelSpeedMultiplier(true, tuning), Epsilon);
            Assert.AreEqual(1f, SteamConcealmentLogic.ChannelSpeedMultiplier(false, tuning), Epsilon);
        }
    }
}
