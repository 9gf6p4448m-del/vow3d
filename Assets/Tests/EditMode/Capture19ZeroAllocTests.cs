using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V9-A18 純邏輯零配置（V090_ENCIRCLE_PLAN.md §3-A，凍結）。只在 dotnet 有鑑別力；Unity 端標 Ignore，理由同
    // CaptureZeroAllocTests.VA25：Unity 的 Mono 上 GC.GetAllocatedBytesForCurrentThread() 恆回 0，
    // Unity 端的零配置由 PlayMode 的 Profiler 探針（V9-C03）負責。
    public sealed class Capture19ZeroAllocTests
    {
        [Test]
#if UNITY_5_3_OR_NEWER
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode profiler probe (V9-C03)")]
#endif
        public void V9A18_EncircleRageKnockoutAndInterrupts_AllocateZeroBytes()
        {
            var logic = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            logic.TryEnterCaptureMode();
            logic.TryStart();
            logic.SeedOwnershipForTest(Capture19Kit.Board(Capture19EncircleTests.S3Blue, Capture19EncircleTests.S3Red));

            long before = GC.GetAllocatedBytesForCurrentThread();

            int damagedCount = 0;
            for (int tick = 1; tick <= 160; tick++)
            {
                int hero = Capture19EncircleTests.S3Hero(tick);
                int opp = Capture19EncircleTests.S3Opponent(tick);
                logic.Tick(0.25f, Capture19Kit.X(hero), Capture19Kit.Z(hero), Capture19Kit.X(opp), Capture19Kit.Z(opp));

                if (tick == 15) logic.SeedScoresForTest(0, 100);
                if (tick == 100) logic.NotifyKnockedOut(CaptureMatchLogic.RedFactionId);
                if (tick >= 50 && tick % 5 == 0 && damagedCount < 20)
                {
                    logic.NotifyDamaged(CaptureMatchLogic.BlueFactionId);
                    damagedCount++;
                }
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(before, after, "160 個 tick（含 BFS、狂怒、倒地/復活）+ 20 次 NotifyDamaged，不得配置任何位元組");
            Assert.AreEqual(20, damagedCount);
            Assert.AreEqual(CaptureMatchState.Active, logic.State, "活性：仍在對局中");
            Assert.GreaterOrEqual(logic.FlipCount, 3, "活性：翻塊");
            Assert.GreaterOrEqual(logic.BlueNeutralizedCount, 2, "活性：藍方中立化");
            Assert.GreaterOrEqual(logic.BlueRageTriggerCount, 2, "活性：狂怒觸發");
            Assert.GreaterOrEqual(logic.ScoreTickCount, 40, "活性：計分");
            Assert.GreaterOrEqual(logic.RespawnCount, 1, "活性：復活");
        }
    }
}
