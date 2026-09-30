using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V14-A14 地熱點時序（docs/V0140_CANYON_PLAN.md §4.5、§9-A，2026-09-30 凍結）。dt＝1/64，GeothermalVentLogic 直接驅動。
    public sealed class CanyonVentTests
    {
        private const float Dt = 1f / 64f;
        private const float G0X = 2.09375f, G0Z = 2.0625f;
        private const float G1X = -2.09375f, G1Z = -2.0625f;
        private const float FarX = 1000f, FarZ = 1000f;

        private static GeothermalVentLogic NewVents()
        {
            return new GeothermalVentLogic(CanyonTerrainSpec.V0140, new CanyonTuning());
        }

        private static void Run(GeothermalVentLogic vents, int ticks, float x, float z, bool canTrigger = true, float dt = Dt)
        {
            for (int i = 0; i < ticks; i++) vents.Tick(dt, x, z, canTrigger);
        }

        [Test]
        public void V14A14_GeothermalVentTiming_ChannelCooldownDamageLeaveIndependentPads()
        {
            // (a) 第 1 個 tick 起在 G0
            var a = NewVents();
            Run(a, 38, G0X, G0Z);
            Assert.AreEqual(0, a.LaunchCount, "(a) 第 38 個 tick 後");
            Assert.AreEqual(0.59375f, a.Progress(0), 0f, "(a) 第 38 個 tick 後");
            Run(a, 1, G0X, G0Z);
            Assert.AreEqual(1, a.LaunchCount, "(a) 第 39 個 tick 後");
            Assert.AreEqual(0, a.LastLaunchPad, "(a)");
            Assert.AreEqual(4.59375f, a.LastLandingX, 0f, "(a)");
            Assert.AreEqual(2.0703125f, a.LastLandingZ, 0f, "(a)");
            Assert.AreEqual(8f, a.Cooldown(0), 0f, "(a) Cooldown(G0)");
            Assert.AreEqual(0f, a.Cooldown(1), 0f, "(a) Cooldown(G1)");

            // (b) 冷卻：(a) 之後仍在 G0
            Run(a, 550 - 39, G0X, G0Z);
            Assert.AreEqual(1, a.LaunchCount, "(b) 第 550 個 tick 後");
            Assert.AreEqual(0f, a.Progress(0), 0f, "(b) 第 550 個 tick 後 Progress(G0)");
            Run(a, 589 - 550 - 1, G0X, G0Z);
            Assert.AreEqual(1, a.LaunchCount, "(b) 第 588 個 tick 後");
            Run(a, 1, G0X, G0Z);
            Assert.AreEqual(2, a.LaunchCount, "(b) 第 589 個 tick 後");

            // (c) 受傷
            var c = NewVents();
            Run(c, 20, G0X, G0Z);
            c.NotifyDamaged();
            Run(c, 1, G0X, G0Z);
            Assert.AreEqual(0f, c.Progress(0), 0f, "(c) 第 21 個 tick 後 Progress");
            Run(c, 59 - 21, G0X, G0Z);
            Assert.AreEqual(0, c.LaunchCount, "(c) 第 59 個 tick 後");
            Assert.AreEqual(0f, c.Cooldown(0), 0f, "(c) 中斷不觸發冷卻");
            Run(c, 1, G0X, G0Z);
            Assert.AreEqual(1, c.LaunchCount, "(c) 第 60 個 tick 後");

            // (d) 離開
            var d = NewVents();
            Run(d, 30, G0X, G0Z);
            Run(d, 1, FarX, FarZ);
            Run(d, 69 - 31, G0X, G0Z);
            Assert.AreEqual(0, d.LaunchCount, "(d) 第 69 個 tick 後");
            Run(d, 1, G0X, G0Z);
            Assert.AreEqual(1, d.LaunchCount, "(d) 第 70 個 tick 後");

            // (e) 各點獨立
            var e = NewVents();
            Run(e, 39, G0X, G0Z);
            Assert.AreEqual(1, e.LaunchCount, "(e) 前提");
            Run(e, 77 - 39, G1X, G1Z);
            Assert.AreEqual(1, e.LaunchCount, "(e) 第 77 個 tick 後");
            Run(e, 1, G1X, G1Z);
            Assert.AreEqual(2, e.LaunchCount, "(e) 第 78 個 tick 後");
            Assert.AreEqual(1, e.LastLaunchPad, "(e)");
            Assert.AreEqual(-4.59375f, e.LastLandingX, 0f, "(e)");
            Assert.AreEqual(-2.0703125f, e.LastLandingZ, 0f, "(e)");

            // (f) 邊界
            var f = NewVents();
            Run(f, 38, 2.46875f, 2.0625f);
            Assert.AreEqual(0, f.LaunchCount, "(f) 距 0.375 第 38 個 tick 後");
            Run(f, 1, 2.46875f, 2.0625f);
            Assert.AreEqual(1, f.LaunchCount, "(f) 距 0.375 第 39 個 tick 發射");
            var f2 = NewVents();
            Run(f2, 100, 2.4765625f, 2.0625f);
            Assert.AreEqual(0, f2.LaunchCount, "(f) 距 0.3828125 跑 100 個 tick");

            // (g) 不可觸發
            var g = NewVents();
            Run(g, 100, G0X, G0Z, false);
            Assert.AreEqual(0, g.LaunchCount, "(g) heroCanTrigger==false");
            Assert.AreEqual(0f, g.Progress(0), 0f, "(g)");
            Run(g, 38, G0X, G0Z, true);
            Assert.AreEqual(0, g.LaunchCount, "(g) 改 true 後第 38 個 tick");
            Run(g, 1, G0X, G0Z, true);
            Assert.AreEqual(1, g.LaunchCount, "(g) 活性：改 true 後第 39 個 tick 發射");

            // (h) dt＝0.25
            var h = NewVents();
            Run(h, 2, G0X, G0Z, true, 0.25f);
            Assert.AreEqual(0, h.LaunchCount, "(h) 第 2 個 tick 後");
            Run(h, 1, G0X, G0Z, true, 0.25f);
            Assert.AreEqual(1, h.LaunchCount, "(h) 第 3 個 tick 後");

            // (i) Reset
            var i = NewVents();
            Run(i, 39, G0X, G0Z);
            Run(i, 20, G1X, G1Z);
            Assert.Greater(i.Cooldown(0), 0f, "(i) 前提：G0 冷卻中");
            Assert.Greater(i.Progress(1), 0f, "(i) 前提：G1 引導中");
            i.Reset();
            Assert.AreEqual(0f, i.Cooldown(0), 0f, "(i)");
            Assert.AreEqual(0f, i.Cooldown(1), 0f, "(i)");
            Assert.AreEqual(0f, i.Progress(0), 0f, "(i)");
            Assert.AreEqual(0f, i.Progress(1), 0f, "(i)");
        }
    }
}
