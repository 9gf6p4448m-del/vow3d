using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V14-A10～A13、A20、A21（docs/V0140_CANYON_PLAN.md §9-A，2026-09-30 凍結）。期望值一律字面值。
    public sealed class CanyonRulesTests
    {
        private const int Blue = CaptureMatchLogic.BlueFactionId;
        private const int Red = CaptureMatchLogic.RedFactionId;
        private const int Neutral = CaptureMatchLogic.NeutralFactionId;

        private static CaptureBoardSpec S => CaptureBoardSpec.V0140Canyon;
        private static CanyonTerrainSpec T => CanyonTerrainSpec.V0140;

        private static int[] AllNeutral()
        {
            int[] owners = new int[19];
            for (int i = 0; i < 19; i++) owners[i] = Neutral;
            return owners;
        }

        private static CaptureMatchLogic NewNeutralActive(CaptureBoardSpec spec)
        {
            var match = new CaptureMatchLogic(new CaptureTuning(), spec);
            Assert.IsTrue(match.TryEnterCaptureMode());
            Assert.IsTrue(match.TryStart());
            match.SeedOwnershipForTest(AllNeutral());
            return match;
        }

        [Test]
        public void V14A10_CoreFollowsSpec_CanyonCentreRadius2_5_FlatFixtureKeepsV0131()
        {
            AbyssalVanguardTuning c = AbyssalVanguardTuning.ForSpec(CaptureBoardSpec.V0140Canyon);
            Assert.AreEqual(0f, c.CoreX, 0f);
            Assert.AreEqual(0f, c.CoreZ, 0f);
            Assert.AreEqual(2.5f, c.CoreRadius, 0f);
            Assert.AreEqual(3.5f, c.CoreChannelSeconds, 0f);

            AbyssalVanguardTuning flat = AbyssalVanguardTuning.ForSpec(CaptureBoardSpec.V0100Sanctuary);
            Assert.AreEqual(4.375f, flat.CoreX, 0f, "活性：兩規格真的不同");
            Assert.AreEqual(1.8f, flat.CoreRadius, 0f, "活性：兩規格真的不同");

            Assert.IsTrue(c.IsInCore(0f, 2.5f));
            Assert.IsFalse(c.IsInCore(0f, 2.5078125f));
            Assert.IsTrue(c.IsInCore(2.5f, 0f));

            for (int i = 0; i < 19; i++)
            {
                double dx = S.CenterX(i) - 0.0, dz = S.CenterZ(i) - 0.0;
                double d = System.Math.Sqrt(dx * dx + dz * dz);
                if (i == 0) Assert.AreEqual(0.0, d, 0.0, "0 號塔心距核心 0");
                else Assert.GreaterOrEqual(d, 5.0, "塔心 " + i + " 的佔塔圈不與核心圈重疊");
            }

            float[] xs = { 0f, 1.5f, 0f, 0f, 2.5f };
            float[] zs = { 0f, 1.5f, 2.5f, 2.5078125f, 0.0078125f };
            bool sawTrue = false, sawFalse = false;
            for (int k = 0; k < xs.Length; k++)
            {
                bool inCore = c.IsInCore(xs[k], zs[k]);
                bool inCircle0 = S.CircleAt(xs[k], zs[k], 2.5f) == 0;
                Assert.AreEqual(inCircle0, inCore, "核心圈與 0 號佔塔圈重合 (" + xs[k] + "," + zs[k] + ")");
                if (inCore) sawTrue = true; else sawFalse = true;
            }
            Assert.IsTrue(sawTrue && sawFalse, "活性：真假兩種都出現");
        }

        [Test]
        public void V14A11a_AttackRange_OnlyAttackerOnCliffGetsTenPercent()
        {
            Assert.AreEqual(5f, CanyonRules.AttackRange(5f, 9.84375f, 5.68359375f, T), 0f, "R0 中心（斜坡，TileAt＝2）不加成");
            Assert.AreEqual(5.5f, CanyonRules.AttackRange(5f, 6.5625f, 3.7890625f, T), 0f, "2 號塔心");
            Assert.AreEqual(5f, CanyonRules.AttackRange(5f, 13.125f, 0f, T), 0f, "10 號塔心");
            Assert.AreEqual(5f, CanyonRules.AttackRange(5f, 0f, 0f, T), 0f, "0 號塔心");
            Assert.AreEqual(5f, CanyonRules.AttackRange(5f, 6.5625f, 3.7890625f, null), 0f, "terrain==null");
            Assert.AreEqual(1.8f * 110 / 100f, CanyonRules.AttackRange(1.8f, 6.5625f, 3.7890625f, T), 0f, "對手出手距離在崖台");
            Assert.AreEqual(1.8f, CanyonRules.AttackRange(1.8f, 13.125f, 0f, T), 0f, "對手出手距離在平原");
        }

        [Test]
        public void V14A11b_InAttackRange_BoundaryInclusive_TargetLayerIrrelevant()
        {
            Assert.AreEqual(2, T.TileAt(8.125f, 0f), "前提：目標在 2 號崖台");
            Assert.IsTrue(CanyonRules.InAttackRange(5f, 13.125f, 0f, 8.125f, 0f, T), "攻擊者 10 號、目標崖台距 5.0 → 在");
            Assert.IsFalse(CanyonRules.InAttackRange(5f, 13.125f, 0f, 8.109375f, 0f, T), "攻擊者 10 號、目標崖台距平方 25.156494 → 不在");

            Assert.IsTrue(CanyonRules.InAttackRange(5f, 6.5625f, 3.7890625f, 12.0625f, 3.7890625f, T), "崖台攻擊者距 5.5 → 在");
            Assert.IsFalse(CanyonRules.InAttackRange(5f, 6.5625f, 3.7890625f, 12.078125f, 3.7890625f, T), "崖台攻擊者距 5.515625 → 不在");

            Assert.IsTrue(CanyonRules.InAttackRange(1.8f, 6.5625f, 3.7890625f, 8.5390625f, 3.7890625f, T), "對手崖台距 1.9765625 → 在");
            Assert.IsFalse(CanyonRules.InAttackRange(1.8f, 6.5625f, 3.7890625f, 8.546875f, 3.7890625f, T), "對手崖台距 1.984375 → 不在");
            Assert.IsTrue(CanyonRules.InAttackRange(1.8f, 13.125f, 0f, 11.328125f, 0f, T), "對手平原距 1.796875 → 在");
            Assert.IsFalse(CanyonRules.InAttackRange(1.8f, 13.125f, 0f, 11.3203125f, 0f, T), "對手平原距 1.8046875 → 不在");
        }

        [Test]
        public void V14A12_VisionComposition_CliffEightMetres_CanyonBlindToCliff_TrueVisionFirst()
        {
            for (int v = 0; v <= 1; v++)
            {
                int other = 1 - v;
                var match = NewNeutralActive(S);
                // (a) 崖台觀看者 8m
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, v, 6.5625f, 3.7890625f, false, 14.5625f, 3.7890625f), "(a) 距 8 side " + v);
                Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, v, 6.5625f, 3.7890625f, false, 14.578125f, 3.7890625f), "(a) 距 8.015625 side " + v);
                // (b) 平原觀看者 6m
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, v, 13.125f, 0f, false, 13.125f, 6.0f), "(b) 距 6 side " + v);
                Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, v, 13.125f, 0f, false, 13.125f, 6.015625f), "(b) 距 6.015625 side " + v);
                // (c) 谷底看崖台
                Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, v, 0f, 0f, false, 4.59375f, 2.0703125f), "(c) 中立時看不到 side " + v);
                int[] owners = AllNeutral();
                owners[2] = v;
                match.SeedOwnershipForTest(owners);
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, v, 0f, 0f, false, 4.59375f, 2.0703125f), "(c) 2 號由觀看方持有 → 看得到 side " + v);
                owners[2] = other;
                match.SeedOwnershipForTest(owners);
                Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, v, 0f, 0f, false, 4.59375f, 2.0703125f), "(c) 2 號由對方持有 → 看不到 side " + v);
                match.SeedOwnershipForTest(AllNeutral());
                // (d) 谷底看平原
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, v, 0f, 7.578125f, false, 4.5f, 10.5f), "(d) side " + v);
                // (e) 崖台看谷底
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, v, 4.59375f, 2.0703125f, false, 0f, 0f), "(e) side " + v);
                // (f) 倒地
                owners = AllNeutral();
                owners[10] = v;
                match.SeedOwnershipForTest(owners);
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(match, v, 13.125f, 1f, true, 13.125f, 0f), "(f) 倒地、持有 → 看得到 side " + v);
                match.SeedOwnershipForTest(AllNeutral());
                Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, v, 13.125f, 1f, true, 13.125f, 0f), "(f) 倒地、不持有 → 看不到 side " + v);

                // (g) 夾具：無崖台加成、無谷底規則
                var flat = NewNeutralActive(CaptureBoardSpec.V0100Sanctuary);
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(flat, v, 6.5625f, 3.7890625f, false, 12.5625f, 3.7890625f), "(g) 距 6 side " + v);
                Assert.IsFalse(CaptureVisibilityLogic.CanSee(flat, v, 6.5625f, 3.7890625f, false, 12.578125f, 3.7890625f), "(g) 距 6.015625 side " + v);
                Assert.IsTrue(CaptureVisibilityLogic.CanSee(flat, v, 0f, 0f, false, 4.59375f, 2.0703125f), "(g) 同 (c) 座標看得到 side " + v);
            }
        }

        [Test]
        public void V14A13_ShallowWater_CanyonCentrePlusOne_AddsWithTideTalent()
        {
            Assert.AreEqual(4f, CanyonRules.WaterRadius(3f, 0f, 0f, -7.578125f, T), 0f, "4 號塔心");
            Assert.AreEqual(3f, CanyonRules.WaterRadius(3f, 0f, 0f, -15.15625f, T), 0f, "13 號塔心");
            Assert.AreEqual(3f, CanyonRules.WaterRadius(3f, 0f, 0f, -11.3671875f, T), 0f, "R5 中心");
            Assert.AreEqual(3f, CanyonRules.WaterRadius(3f, 0f, 6.5625f, 3.7890625f, T), 0f, "2 號塔心");
            Assert.AreEqual(6f, CanyonRules.WaterRadius(3f, 2f, 0f, -7.578125f, T), 0f, "潮汐＋4 號塔心");
            Assert.AreEqual(5f, CanyonRules.WaterRadius(3f, 2f, 0f, -15.15625f, T), 0f, "潮汐＋13 號塔心");
            Assert.AreEqual(3f, CanyonRules.WaterRadius(3f, 0f, 0f, -7.578125f, null), 0f, "terrain==null");
            Assert.AreEqual(5f, CanyonRules.WaterRadius(3f, 2f, 0f, -7.578125f, null), 0f, "terrain==null＋潮汐");
        }

        [Test]
        public void V14A20_FireReveal_AttackerRevealedToVictimSide_OneAndAHalfSeconds_NoStacking()
        {
            var match = NewNeutralActive(S);
            var tr = new RevealTracker(1.5f);

            // ① 谷底紅方看崖台藍英雄
            Assert.IsFalse(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "① 命中前看不到");
            tr.NotifyHit(Blue, Red);
            Assert.IsTrue(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "① NotifyHit(藍,紅) 後立刻看得到");

            // ② 受害方不顯形
            Assert.IsFalse(See(match, tr, RevealUnit.RedOpponent, Blue, 0f, -7.578125f, false, -4.59375f, -2.0703125f), "② 受害方的單位不因被打而顯形");
            tr.NotifyHit(Red, Blue);
            Assert.IsTrue(See(match, tr, RevealUnit.RedOpponent, Blue, 0f, -7.578125f, false, -4.59375f, -2.0703125f), "② 活性：紅方命中藍方後看得到");
            tr.Reset();
            tr.NotifyHit(Blue, Red);

            // ③ 計時
            for (int tick = 1; tick <= 5; tick++) tr.Tick(0.25f);
            Assert.IsTrue(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "③ 第 5 個 tick 後仍看得到");
            tr.Tick(0.25f);
            Assert.IsFalse(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "③ 第 6 個 tick 後看不到");

            // ④ 重設不累加
            tr.NotifyHit(Blue, Red);
            for (int tick = 1; tick <= 4; tick++) tr.Tick(0.25f);
            tr.NotifyHit(Blue, Red);
            for (int tick = 5; tick <= 9; tick++) tr.Tick(0.25f);
            Assert.IsTrue(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "④ 第 9 個 tick 後仍看得到");
            tr.Tick(0.25f);
            Assert.IsFalse(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "④ 第 10 個 tick 後看不到");

            // ⑤ 倒地觀看者、顯形中
            tr.NotifyHit(Blue, Red);
            Assert.IsTrue(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, true, 4.59375f, 2.0703125f), "⑤ 倒地仍看得到顯形目標");

            // ⑥ Reset
            tr.Reset();
            Assert.AreEqual(0f, tr.Remaining(RevealUnit.BlueHero), 0f, "⑥");
            Assert.IsFalse(See(match, tr, RevealUnit.BlueHero, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "⑥ Reset 後看不到");

            // ⑦ 舊多載不受命中影響
            tr.NotifyHit(Blue, Red);
            Assert.IsFalse(CaptureVisibilityLogic.CanSee(match, Red, 0f, 0f, false, 4.59375f, 2.0703125f), "⑦ 舊多載看不到");

            // ⑧ 夾具
            var flat = NewNeutralActive(CaptureBoardSpec.V0100Sanctuary);
            var tr2 = new RevealTracker(1.5f);
            Assert.IsFalse(See(flat, tr2, RevealUnit.BlueHero, Red, 0f, 0f, false, 13.125f, 0f), "⑧ 命中前看不到");
            tr2.NotifyHit(Blue, Red);
            Assert.IsTrue(See(flat, tr2, RevealUnit.BlueHero, Red, 0f, 0f, false, 13.125f, 0f), "⑧ 命中後看得到");
        }

        private static bool See(CaptureMatchLogic match, RevealTracker tr, RevealUnit unit, int viewerSide,
                                float vx, float vz, bool ko, float tx, float tz)
        {
            return CaptureVisibilityLogic.CanSee(match, tr, unit, viewerSide, vx, vz, ko, tx, tz);
        }

        [Test]
        public void V14A21_OpponentTargetSelection_WalkDistanceOnCanyon_StraightLineOnFixture()
        {
            int[] owners = new int[19];
            for (int i = 0; i < 19; i++) owners[i] = Red;
            owners[2] = Neutral;
            owners[13] = Neutral;
            Assert.AreEqual(13, CaptureOpponentPolicy.SelectTargetTile(0f, 0f, owners, S, T), "峽谷：走路 15.16 vs 37.89 → 13");
            Assert.AreEqual(2, CaptureOpponentPolicy.SelectTargetTile(0f, 0f, owners, S, null), "terrain=null：直線 7.58 vs 15.16 → 2");

            for (int i = 0; i < 19; i++) owners[i] = Red;
            owners[2] = Neutral;
            owners[12] = Neutral;
            Assert.AreEqual(-1, S.TileAt(0f, 19f), "前提：(0,19) 在棋盤外");
            Assert.AreEqual(2, CaptureOpponentPolicy.SelectTargetTile(0f, 19f, owners, S, T), "起點塊 −1 → 取最近塔心 7：到 2 走 22.73、到 12 走 37.89");
        }
    }
}
