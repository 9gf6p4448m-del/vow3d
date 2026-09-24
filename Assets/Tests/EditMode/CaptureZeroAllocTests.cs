using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V080_CAPTURE_PLAN.md §3-A：V-A25（純邏輯零配置，只在 dotnet 有鑑別力；Unity 端標 Ignore 並寫明理由，
    // 理由與作法比照 GridNavigatorTests.cs 既有的三條——Unity 的 Mono 上
    // GC.GetAllocatedBytesForCurrentThread() 恆回 0，Unity 端的零配置由 PlayMode 的 Profiler 探針負責）。
    public sealed class CaptureZeroAllocTests
    {
        [Test]
#if UNITY_5_3_OR_NEWER
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode profiler probe")]
#endif
        public void VA25_W1PlusInterruptsPlusAKnockoutCycle_AllocateZeroBytes()
        {
            var logic = new CaptureMatchLogic(new CaptureTuning());
            logic.TryEnterCaptureMode();
            logic.TryStart();

            long before = GC.GetAllocatedBytesForCurrentThread();

            int damagedCount = 0;
            for (int tick = 1; tick <= 1008; tick++)
            {
                // 英雄一直站在 0 號塔心（W1：第 1~14 個 tick 翻下 0 號後不動）；對手固定不在任何光圈內。
                logic.Tick(0.25f, 0f, 0f, 1000f, 1000f);

                if (tick == 500) logic.NotifyKnockedOut(CaptureMatchLogic.RedFactionId);

                // 20 次 NotifyDamaged：都在 0 號已經翻藍之後（tick>14），只會打斷「重複引導已擁有的塊」，
                // 不影響最終歸屬／比分。
                if (tick > 14 && tick % 45 == 0 && damagedCount < 20)
                {
                    logic.NotifyDamaged(CaptureMatchLogic.BlueFactionId);
                    damagedCount++;
                }
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(before, after, "1008 個 tick + 20 次 NotifyDamaged + 1 次倒地/復活，不得配置任何位元組");
            Assert.AreEqual(20, damagedCount);
            Assert.GreaterOrEqual(logic.FlipCount, 1, "活性：翻塊");
            Assert.GreaterOrEqual(logic.ScoreTickCount, 252, "活性：計分");
            Assert.GreaterOrEqual(logic.RespawnCount, 1, "活性：復活");
        }
    }
}
