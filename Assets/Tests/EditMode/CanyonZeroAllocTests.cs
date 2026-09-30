using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V14-A17 純邏輯零配置（docs/V0140_CANYON_PLAN.md §9-A，2026-09-30 凍結）。只在 dotnet 有鑑別力；Unity 端標 Ignore，
    // 理由同 Capture19ZeroAllocTests：Unity 的 Mono 上 GC.GetAllocatedBytesForCurrentThread() 恆回 0，
    // Unity 端的零配置由 PlayMode 的 ProfilerRecorder 探針負責。
    public sealed class CanyonZeroAllocTests
    {
        // 取樣點：谷底、平原、崖台、R5 坡面（陣列在量測窗口外配置）。
        private static readonly float[] Hx = { 0f, 13.125f, 6.5625f, 0f };
        private static readonly float[] Hz = { 0f, 0f, 3.7890625f, -11.3671875f };

        [Test]
#if UNITY_5_3_OR_NEWER
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode ProfilerRecorder probe")]
#endif
        public void V14A17_CanyonPureLogic_TenThousandCallsEach_AllocateZeroBytes()
        {
            CanyonTerrainSpec t = CanyonTerrainSpec.V0140;
            var match = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0140Canyon);
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsTrue(match.TryStart());
            int[] owners = new int[19];
            for (int i = 0; i < 19; i++) owners[i] = CaptureMatchLogic.NeutralFactionId;
            match.SeedOwnershipForTest(owners);
            var vents = new GeothermalVentLogic(t, new CanyonTuning());
            var tracker = new RevealTracker(1.5f);
            BlockGrid grid = CanyonKit.NewArenaGrid();
            t.StampCliffs(grid, 0.35f, +1);
            grid.StampBox(3.625f, 0f, 1f, 0f, 2f, 0.3f, 0.35f, +1);
            int[] route = new int[19];

            // 暖身：讓 JIT、靜態建構子等一次性配置發生在量測窗口之外。
            RunAll(t, match, vents, tracker, grid, route, 1, out _, out _, out _, out _, out _, out _, out _, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunAll(t, match, vents, tracker, grid, route, 10000,
                   out bool sawLow, out bool sawMid, out bool sawHigh, out bool sawRamp,
                   out bool sawSeeTrue, out bool sawSeeFalse, out int launches, out int routeLength);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(before, after, "每個函式各呼叫 10000 次，不得配置任何位元組");
            Assert.IsTrue(sawLow && sawMid && sawHigh && sawRamp, "活性：HeightAt 三種高度與坡面值都出現");
            Assert.IsTrue(sawSeeTrue && sawSeeFalse, "活性：CanSee 真假都出現");
            Assert.GreaterOrEqual(launches, 1, "活性：地熱點發射 ≥ 1 次");
            Assert.AreEqual(5, routeLength, "活性：FindWalkRoute 真的有算出路線");
        }

        private static void RunAll(CanyonTerrainSpec t, CaptureMatchLogic match, GeothermalVentLogic vents, RevealTracker tracker,
                                   BlockGrid grid, int[] route, int calls,
                                   out bool sawLow, out bool sawMid, out bool sawHigh, out bool sawRamp,
                                   out bool sawSeeTrue, out bool sawSeeFalse, out int launches, out int routeLength)
        {
            float[] hx = Hx;
            float[] hz = Hz;
            sawLow = sawMid = sawHigh = sawRamp = false;
            sawSeeTrue = sawSeeFalse = false;
            routeLength = 0;
            int sameFloor = 0;
            float sum = 0f;
            int launchBase = vents.LaunchCount;

            for (int i = 0; i < calls; i++)
            {
                int k = i & 3;
                float h = t.HeightAt(hx[k], hz[k], 0);
                if (h == -1f) sawLow = true;
                else if (h == 0f) sawMid = true;
                else if (h == 1f) sawHigh = true;
                else sawRamp = true;
            }
            for (int i = 0; i < calls; i++) sum += (float)t.ClassAt(hx[i & 3], hz[i & 3], 0);
            for (int i = 0; i < calls; i++) if (t.IsSameFloor(0f, 0f, 0, (i & 1) == 0 ? 0f : 6.5625f, (i & 1) == 0 ? -7.578125f : 3.7890625f, 0)) sameFloor++;
            for (int i = 0; i < calls; i++)
            {
                bool see = CaptureVisibilityLogic.CanSee(match, tracker, RevealUnit.BlueHero, 1, 0f, 0f, false,
                                                         (i & 1) == 0 ? 4.59375f : 1f, (i & 1) == 0 ? 2.0703125f : 0f);
                if (see) sawSeeTrue = true; else sawSeeFalse = true;
            }
            for (int i = 0; i < calls; i++)
            {
                bool see = CaptureVisibilityLogic.CanSee(match, 1, 0f, 0f, false, (i & 1) == 0 ? 4.59375f : 1f, (i & 1) == 0 ? 2.0703125f : 0f);
                if (see) sawSeeTrue = true; else sawSeeFalse = true;
            }
            for (int i = 0; i < calls; i++) sum += CanyonRules.AttackRange(5f, hx[i & 3], hz[i & 3], t);
            for (int i = 0; i < calls; i++) if (CanyonRules.InAttackRange(5f, 13.125f, 0f, 8.125f, 0f, t)) sameFloor++;
            for (int i = 0; i < calls; i++) sum += CanyonRules.WaterRadius(3f, 2f, hx[i & 3], hz[i & 3], t);
            for (int i = 0; i < calls; i++) vents.Tick(1f / 64f, 2.09375f, 2.0625f, true);
            for (int i = 0; i < calls; i++)
            {
                if ((i & 7) == 0) tracker.NotifyHit(0, 1);
                tracker.Tick(0.25f);
                if (tracker.IsRevealedTo(RevealUnit.BlueHero, 1)) sameFloor++;
            }
            for (int i = 0; i < calls; i++) if (grid.TryFindNearestFreeSameFloor(3.875f, 0f, 20, t, 0, out int cx, out int cz)) sameFloor += cx + cz;
            for (int i = 0; i < calls; i++) routeLength = t.FindWalkRoute(2, 13, route);

            launches = vents.LaunchCount - launchBase;
            if (sum < -1e30f || sameFloor < 0) throw new InvalidOperationException("unreachable: keeps results alive");
        }
    }
}
