using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-t～V2-x：縛足→減速時序（單一單位的狀態機）。
    public sealed class QuicksandStatusLogicTests
    {
        private const float Epsilon = 1e-4f;
        private const float FrameDt = 1f / 60f;

        // V2-t Entering_RootsForOnePointTwoSeconds_ThenSlowsToSixtyFivePercent
        [Test]
        public void Entering_RootsForOnePointTwoSeconds_ThenSlowsToSixtyFivePercent()
        {
            ElementTuning tuning = new ElementTuning();
            QuicksandStatusLogic logic = new QuicksandStatusLogic(tuning);

            logic.Tick(FrameDt, 7);

            Assert.IsTrue(logic.IsRooted, "進入敵對流沙的當幀必須立刻開始縛足");
            // 期望值刻意寫死 0.65（不讀 tuning.QuicksandSlowMultiplier）：
            // 若兩邊都讀同一個 tuning 欄位，數值被突變時期望值會跟著漂移，測試永遠綠燈、抓不到 E3。
            Assert.AreEqual(0.65f, logic.SpeedMultiplier, Epsilon,
                "縛足期間的速度倍率也是 0.65（外層另有 IsRooted 擋掉全部位移）");
        }

        // V2-u AtOnePointOneNineSeconds_StillRooted_AtOnePointTwoOne_ItMovesAtSixtyFivePercent
        [Test]
        public void AtOnePointOneNineSeconds_StillRooted_AtOnePointTwoOne_ItMovesAtSixtyFivePercent()
        {
            ElementTuning tuning = new ElementTuning();
            QuicksandStatusLogic logic = new QuicksandStatusLogic(tuning);

            logic.Tick(1.19f, 7);
            Assert.IsTrue(logic.IsRooted, "累計 1.19s（< 1.2s）仍應縛足中");

            logic.Tick(0.02f, 7); // 累計 1.21s
            Assert.IsFalse(logic.IsRooted, "累計 1.21s（> 1.2s）縛足必須結束");
            Assert.AreEqual(0.65f, logic.SpeedMultiplier, Epsilon,
                "縛足結束後只要還在區內就是 0.65 減速");
        }

        // V2-v LeavingAndReentering_TheSameQuicksand_DoesNotRootAgain
        [Test]
        public void LeavingAndReentering_TheSameQuicksand_DoesNotRootAgain()
        {
            ElementTuning tuning = new ElementTuning();
            QuicksandStatusLogic logic = new QuicksandStatusLogic(tuning);

            logic.Tick(2f, 7); // 縛足直接跑完（2s > 1.2s 的縛足時長）
            Assert.IsFalse(logic.IsRooted);

            logic.Tick(FrameDt, -1); // 離開
            Assert.AreEqual(1f, logic.SpeedMultiplier, Epsilon);
            Assert.IsFalse(logic.IsRooted);

            logic.Tick(FrameDt, 7); // 走回同一個流沙
            Assert.IsFalse(logic.IsRooted, "同一個流沙對同一單位只縛足一次");
            Assert.AreEqual(0.65f, logic.SpeedMultiplier, Epsilon);
        }

        // V2-w EnteringADifferentQuicksand_RootsAgain
        [Test]
        public void EnteringADifferentQuicksand_RootsAgain()
        {
            ElementTuning tuning = new ElementTuning();
            QuicksandStatusLogic logic = new QuicksandStatusLogic(tuning);

            logic.Tick(2f, 7);
            logic.Tick(FrameDt, -1); // 離開 7

            logic.Tick(FrameDt, 9); // 進入不同的流沙 9
            Assert.IsTrue(logic.IsRooted, "換一個流沙必須重新縛足");
        }

        // V2-x LeavingTheQuicksand_RestoresFullSpeedImmediately
        [Test]
        public void LeavingTheQuicksand_RestoresFullSpeedImmediately()
        {
            ElementTuning tuning = new ElementTuning();
            QuicksandStatusLogic logic = new QuicksandStatusLogic(tuning);

            logic.Tick(2f, 7); // 縛足跑完，此刻是 0.65 減速中
            Assert.AreEqual(0.65f, logic.SpeedMultiplier, Epsilon);

            logic.Tick(FrameDt, -1);
            Assert.AreEqual(1f, logic.SpeedMultiplier, Epsilon, "離開流沙當幀必須立刻恢復滿速");
        }
    }
}
