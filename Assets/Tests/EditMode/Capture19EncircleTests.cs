using System.Collections.Generic;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V9 純邏輯測試共用的字面資料與走位工具（V090_ENCIRCLE_PLAN.md §3-A）。
    // 期望值一律寫死字面值：塔心表照抄 E3，不讀 CaptureTuning／CaptureBoardSpec／HexBoardLayout 的欄位。
    internal static class Capture19Kit
    {
        public const int Blue = 0;
        public const int Red = 1;
        public const int Neutral = 2;
        public const int Far = -1;          // 「遠處」＝(1000,1000)
        public const float Dt = 0.25f;

        // E3 字面座標（索引 0～18）。
        public static readonly float[] CX =
        {
            0f, 0f, 6.5625f, 6.5625f, 0f, -6.5625f, -6.5625f,
            0f, 6.5625f, 13.125f, 13.125f, 13.125f, 6.5625f, 0f, -6.5625f, -13.125f, -13.125f, -13.125f, -6.5625f,
        };
        public static readonly float[] CZ =
        {
            0f, 7.578125f, 3.7890625f, -3.7890625f, -7.578125f, -3.7890625f, 3.7890625f,
            15.15625f, 11.3671875f, 7.578125f, 0f, -7.578125f, -11.3671875f, -15.15625f, -11.3671875f, -7.578125f, 0f, 7.578125f, 11.3671875f,
        };

        public static float X(int tile) => tile == Far ? 1000f : CX[tile];
        public static float Z(int tile) => tile == Far ? 1000f : CZ[tile];

        // L＝new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen)，TryEnterCaptureMode＋TryStart 之後。
        public static CaptureMatchLogic NewActive19()
        {
            var logic = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(logic.TryEnterCaptureMode());
            Assert.IsTrue(logic.TryStart());
            return logic;
        }

        // 「藍 {…} 紅 {…}」：用 SeedOwnershipForTest 寫入，未列出的塊為中立。
        public static CaptureMatchLogic NewSeeded19(int[] blue, int[] red)
        {
            var logic = NewActive19();
            logic.SeedOwnershipForTest(Board(blue, red));
            return logic;
        }

        public static int[] Board(int[] blue, int[] red)
        {
            var owners = new int[19];
            for (int i = 0; i < 19; i++) owners[i] = Neutral;
            foreach (int t in blue) owners[t] = Blue;
            foreach (int t in red) owners[t] = Red;
            return owners;
        }

        public static void Step(CaptureMatchLogic logic, int heroTile, int opponentTile)
        {
            logic.Tick(Dt, X(heroTile), Z(heroTile), X(opponentTile), Z(opponentTile));
        }

        public static int[] Held(CaptureMatchLogic logic, int side)
        {
            var held = new List<int>();
            for (int i = 0; i < 19; i++) if (logic.OwnerOf(i) == side) held.Add(i);
            return held.ToArray();
        }
    }

    // V9-A06～V9-A14：包夾 BFS、斷能、狂怒、復活優先序（§3-A，凍結；dt＝0.25）。
    public sealed class Capture19EncircleTests
    {
        private const int Blue = Capture19Kit.Blue;
        private const int Red = Capture19Kit.Red;
        private const int Neutral = Capture19Kit.Neutral;
        private const int Far = Capture19Kit.Far;

        private static void Step(CaptureMatchLogic l, int hero, int opp) => Capture19Kit.Step(l, hero, opp);
        private static int[] Held(CaptureMatchLogic l, int side) => Capture19Kit.Held(l, side);

        // ── V9-A06 孤島翻塊當場中立化（S1） ──
        [Test]
        public void V9A06_IslandFlip_IsNeutralizedOnTheSameTick_ControlAdjacentToMotherStays()
        {
            var l = Capture19Kit.NewActive19();
            for (int t = 1; t <= 13; t++) Step(l, 0, Far);
            Assert.AreEqual(Neutral, l.OwnerOf(0), "t13 0 號中立");
            Assert.AreEqual(3.25f, l.BlueChannelProgress, "t13 藍方進度");
            Step(l, 0, Far); // t14
            Assert.AreEqual(Neutral, l.OwnerOf(0), "t14 0 號剛翻下就中立化");
            Assert.AreEqual(1, l.FlipCount, "t14 FlipCount（活性：真的翻過）");
            Assert.AreEqual(1, l.BlueNeutralizedCount, "t14 藍方中立化數");
            Assert.AreEqual(0f, l.BlueRageRemaining, "t14 藍方狂怒");
            for (int t = 15; t <= 16; t++) Step(l, 0, Far);
            Assert.AreEqual(24, l.BlueScore, "t16 藍分");
            Assert.AreEqual(24, l.RedScore, "t16 紅分");
            for (int t = 17; t <= 28; t++) Step(l, 0, Far);
            Assert.AreEqual(2, l.FlipCount, "t28 FlipCount");
            Assert.AreEqual(2, l.BlueNeutralizedCount, "t28 藍方中立化數");

            // 對照組：4 號鄰接藍方母板塊，翻下後留藍。
            var c = Capture19Kit.NewActive19();
            for (int t = 1; t <= 14; t++) Step(c, 4, Far);
            Assert.AreEqual(Blue, c.OwnerOf(4), "對照 t14 4 號為藍");
            for (int t = 15; t <= 16; t++) Step(c, 4, Far);
            Assert.AreEqual(26, c.BlueScore, "對照 t16 藍分");
            Assert.AreEqual(24, c.RedScore, "對照 t16 紅分");
        }

        private static readonly int[] S2Blue = { 12, 13, 14, 4, 0 };
        private static readonly int[] S2Red = { 7, 8, 18, 1, 2, 3 };

        // ── V9-A07 斷線中立化與順序（S2） ──
        [Test]
        public void V9A07_CutOffTile_IsNeutralizedBeforeScoring_OnTheFlipTick()
        {
            var l = Capture19Kit.NewSeeded19(S2Blue, S2Red);
            for (int t = 1; t <= 15; t++) Step(l, Far, t <= 2 ? Far : 4);
            Assert.AreEqual(Blue, l.OwnerOf(4), "t15 4 號仍藍");
            Step(l, Far, 4); // t16
            Assert.AreEqual(Red, l.OwnerOf(4), "t16 4 號為紅");
            Assert.AreEqual(Neutral, l.OwnerOf(0), "t16 0 號中立");
            Assert.AreEqual(1, l.BlueNeutralizedCount, "t16 藍方中立化數");
            Assert.AreEqual(0, l.RedNeutralizedCount, "t16 紅方中立化數");
            Assert.AreEqual(36, l.BlueScore, "t16 藍分");
            Assert.AreEqual(50, l.RedScore, "t16 紅分");
            Assert.AreEqual(12.0f, l.BlueRageRemaining, "t16 藍方狂怒");
            Assert.AreEqual(0f, l.RedRageRemaining, "t16 紅方狂怒");
        }

        // ── V9-A08 落後判定邊界 ──
        // 四組種子寫在同一條測試裡（不用 TestCase：mutation_check.py 以測試方法名比對 must_fail）。
        [Test]
        public void V9A08_DeficitThreshold_IsStrictlyMoreThanFifteenPercentOfTheLeader()
        {
            // { 種子藍, 種子紅, 期望藍方狂怒×4（整數化）, 期望藍分, 期望紅分 }
            int[][] cases =
            {
                new[] { 100, 100, 0, 106, 114 },
                new[] { 85, 100, 0, 91, 114 },
                new[] { 84, 100, 48, 90, 114 },
                new[] { 0, 0, 0, 6, 14 },
            };
            foreach (int[] c in cases)
            {
                string tag = "(" + c[0] + "," + c[1] + ") ";
                var l = Capture19Kit.NewSeeded19(S2Blue, S2Red);
                for (int t = 1; t <= 15; t++) Step(l, Far, t <= 2 ? Far : 4);
                l.SeedScoresForTest(c[0], c[1]);
                Step(l, Far, 4); // t16
                Assert.AreEqual(1, l.BlueCutEventCount, tag + "活性：t16 確實發生了藍方斷能");
                Assert.AreEqual(c[2] / 4f, l.BlueRageRemaining, tag + "t16 藍方狂怒");
                Assert.AreEqual(c[3], l.BlueScore, tag + "t16 藍分");
                Assert.AreEqual(c[4], l.RedScore, tag + "t16 紅分");
            }
        }

        // V9-A09 的盤面與走位（A14、A18 共用）。
        internal static readonly int[] S3Blue = { 12, 13, 14, 4, 0, 15, 16 };
        internal static readonly int[] S3Red = { 7, 8, 18, 1, 6, 5, 2, 3 };

        internal static int S3Opponent(int t) => t >= 3 && t <= 16 ? 4 : t >= 17 && t <= 30 ? 15 : Far;
        internal static int S3Hero(int t) => t >= 31 ? 11 : Far;

        // ── V9-A09 狂怒時長與刷新（S3） ──
        [Test]
        public void V9A09_RageLastsTwelveSeconds_RefreshesOnASecondCut_NotOnAnIslandFlip_AndClampsAtZero()
        {
            var l = Capture19Kit.NewSeeded19(S3Blue, S3Red);
            var expectedRage = new Dictionary<int, float>
            {
                { 15, 0f }, { 16, 12.0f }, { 17, 11.75f }, { 29, 8.75f }, { 30, 12.0f },
                { 44, 8.5f }, { 77, 0.25f }, { 78, 0f }, { 79, 0f },
            };
            var expectedHeld = new Dictionary<int, int[]>
            {
                { 16, new[] { 12, 13, 14, 15, 16 } },
                { 30, new[] { 12, 13, 14 } },
                { 44, new[] { 11, 12, 13, 14 } },
            };
            for (int t = 1; t <= 79; t++)
            {
                Step(l, S3Hero(t), S3Opponent(t));
                if (t == 15) l.SeedScoresForTest(0, 100);
                if (expectedRage.TryGetValue(t, out float rage))
                    Assert.AreEqual(rage, l.BlueRageRemaining, "t" + t + " 藍方狂怒");
                if (expectedHeld.TryGetValue(t, out int[] held))
                    CollectionAssert.AreEqual(held, Held(l, Blue), "t" + t + " 藍方持有");
            }
            Assert.AreEqual(CaptureMatchState.Active, l.State, "活性：t79 仍在對局中");
            Assert.AreEqual(2, l.BlueRageTriggerCount, "活性：兩次觸發");
        }

        private static readonly int[] S4Blue = { 13, 4, 0 };
        private static readonly int[] S4Red = { 7, 8, 18, 1, 2, 3, 12, 6, 5, 14 };

        // ── V9-A10 母板塊全失與復活地點（S4a） ──
        [Test]
        public void V9A10_AllMothersLost_NeutralizesEverything_AndRespawnGoesToTheEdge_IncludingOnTheSameTick()
        {
            var l = Capture19Kit.NewSeeded19(S4Blue, S4Red);
            l.NotifyKnockedOut(Blue);
            for (int t = 1; t <= 13; t++) Step(l, Far, 13);
            CollectionAssert.AreEqual(new[] { 0, 4, 13 }, Held(l, Blue), "t13 藍方持有");
            Step(l, Far, 13); // t14
            CollectionAssert.AreEqual(new int[0], Held(l, Blue), "t14 藍方持有");
            Assert.AreEqual(2, l.BlueNeutralizedCount, "t14 藍方中立化數");
            Assert.AreEqual(12.0f, l.BlueRageRemaining, "t14 藍方狂怒");
            Assert.AreEqual(18, l.BlueScore, "t14 當時藍分");
            Assert.AreEqual(60, l.RedScore, "t14 當時紅分");
            for (int t = 15; t <= 19; t++) Step(l, Far, 13);
            Assert.IsFalse(l.TryConsumeBlueRespawn(out _, out _), "t19 尚未復活");
            Step(l, Far, 13); // t20
            Assert.IsTrue(l.TryConsumeBlueRespawn(out float x, out float z), "t20 取到復活事件");
            Assert.AreEqual(-17f, x, "t20 復活 x");
            Assert.AreEqual(-17f, z, "t20 復活 z");

            // 同 tick 組：第 20 個 tick 同時翻塊與倒數到期。
            var s = Capture19Kit.NewSeeded19(S4Blue, S4Red);
            s.NotifyKnockedOut(Blue);
            for (int t = 1; t <= 19; t++) Step(s, Far, t >= 7 ? 13 : Far);
            Assert.AreEqual(Blue, s.OwnerOf(13), "同 tick 組 t19 13 號仍藍");
            Assert.IsFalse(s.TryConsumeBlueRespawn(out _, out _), "同 tick 組 t19 尚未復活");
            Step(s, Far, 13); // t20
            Assert.AreEqual(Red, s.OwnerOf(13), "活性：同 tick 組 t20 13 號翻紅");
            Assert.IsTrue(s.TryConsumeBlueRespawn(out float sx, out float sz), "同 tick 組 t20 取到復活事件");
            Assert.AreEqual(-17f, sx, "同 tick 組復活 x");
            Assert.AreEqual(-17f, sz, "同 tick 組復活 z");
        }

        // ── V9-A11 復活優先序 ──
        [Test]
        public void V9A11_RespawnPriority_BlueThirteenTwelveFourteen_RedSevenEighteenEight()
        {
            AssertRespawn(new[] { 12, 13, 14 }, new[] { 7, 8, 18 }, Blue, 0f, -16.65625f);
            AssertRespawn(new[] { 12, 14 }, new[] { 7, 8, 18 }, Blue, 6.5625f, -12.8671875f);
            AssertRespawn(new[] { 14 }, new[] { 7, 8, 18 }, Blue, -6.5625f, -12.8671875f);
            AssertRespawn(new[] { 12, 13, 14 }, new[] { 18, 8 }, Red, -6.5625f, 12.8671875f);
        }

        private static void AssertRespawn(int[] blue, int[] red, int side, float expectedX, float expectedZ)
        {
            string tag = (side == Blue ? "藍" : "紅") + " 藍{" + string.Join(",", blue) + "} 紅{" + string.Join(",", red) + "} ";
            var l = Capture19Kit.NewSeeded19(blue, red);
            l.NotifyKnockedOut(side);
            for (int t = 1; t <= 20; t++) Step(l, Far, Far);
            float x, z;
            bool got = side == Blue ? l.TryConsumeBlueRespawn(out x, out z) : l.TryConsumeRedRespawn(out x, out z);
            Assert.IsTrue(got, tag + "t20 取到復活事件");
            Assert.AreEqual(expectedX, x, tag + "復活 x");
            Assert.AreEqual(expectedZ, z, tag + "復活 z");
        }

        private static readonly int[] S5Blue = { 12, 13, 14, 3, 10, 15, 16 };
        private static readonly int[] S5Red = { 7, 8, 18, 2, 6, 5 };

        private static CaptureMatchLogic RunS5(bool seed, int seedBlue, int seedRed)
        {
            var l = Capture19Kit.NewSeeded19(S5Blue, S5Red);
            for (int t = 1; t <= 16; t++)
            {
                Step(l, t >= 3 ? 6 : Far, t >= 3 ? 3 : Far);
                if (t == 15 && seed) l.SeedScoresForTest(seedBlue, seedRed);
            }
            Assert.AreEqual(2, l.FlipCount, "活性：雙方都在 t16 翻塊");
            return l;
        }

        // ── V9-A12 雙方同 tick 斷能（S5） ──
        [Test]
        public void V9A12_BothSidesCutOnTheSameTick_BothBfsRun_AtMostTheTrailingSideRages()
        {
            var l = RunS5(false, 0, 0);
            CollectionAssert.AreEqual(new[] { 6, 12, 13, 14, 15, 16 }, Held(l, Blue), "t16 藍方持有");
            CollectionAssert.AreEqual(new[] { 2, 3, 7, 8, 18 }, Held(l, Red), "t16 紅方持有");
            Assert.AreEqual(1, l.BlueNeutralizedCount, "t16 藍方中立化數");
            Assert.AreEqual(1, l.RedNeutralizedCount, "t16 紅方中立化數");
            Assert.AreEqual(54, l.BlueScore, "t16 藍分");
            Assert.AreEqual(46, l.RedScore, "t16 紅分");
            Assert.AreEqual(0f, l.BlueRageRemaining, "t16 藍方狂怒");
            Assert.AreEqual(0f, l.RedRageRemaining, "t16 紅方狂怒");

            var a = RunS5(true, 100, 84);
            Assert.AreEqual(12.0f, a.RedRageRemaining, "(100,84) 紅方狂怒");
            Assert.AreEqual(0f, a.BlueRageRemaining, "(100,84) 藍方狂怒");
            Assert.AreEqual(112, a.BlueScore, "(100,84) 藍分");
            Assert.AreEqual(94, a.RedScore, "(100,84) 紅分");

            var b = RunS5(true, 84, 100);
            Assert.AreEqual(12.0f, b.BlueRageRemaining, "(84,100) 藍方狂怒");
            Assert.AreEqual(0f, b.RedRageRemaining, "(84,100) 紅方狂怒");
            Assert.AreEqual(96, b.BlueScore, "(84,100) 藍分");
            Assert.AreEqual(110, b.RedScore, "(84,100) 紅分");
        }

        // ── V9-A13 站在被斷的塊上、孤島翻塊不算斷能（S6＋S1b） ──
        [Test]
        public void V9A13_BfsDoesNotTouchChannelState_AndAnIslandFlipIsNotACutEvent()
        {
            var l = Capture19Kit.NewSeeded19(S2Blue, S2Red);
            for (int t = 1; t <= 16; t++) Step(l, 0, t >= 3 ? 4 : Far);
            Assert.AreEqual(Neutral, l.OwnerOf(0), "t16 0 號中立");
            Assert.AreEqual(-1, l.BlueChannelingTile, "t16 BlueChannelingTile");
            Assert.AreEqual(12.0f, l.BlueRageRemaining, "t16 藍方狂怒");
            Step(l, 0, Far); // t17
            Assert.AreEqual(0, l.BlueChannelingTile, "t17 BlueChannelingTile");
            Assert.AreEqual(0.25f, l.BlueChannelProgress, "t17 進度");
            for (int t = 18; t <= 29; t++) Step(l, 0, Far);
            Assert.AreEqual(3.25f, l.BlueChannelProgress, "t29 進度");
            Assert.AreEqual(8.75f, l.BlueRageRemaining, "t29 狂怒");
            Step(l, 0, Far); // t30
            Assert.AreEqual(Neutral, l.OwnerOf(0), "t30 0 號中立");
            Assert.AreEqual(2, l.BlueNeutralizedCount, "t30 藍方中立化數");
            Assert.AreEqual(8.5f, l.BlueRageRemaining, "t30 狂怒（孤島翻塊不刷新）");

            // S1b：開局盤面，孤島翻塊時嚴重落後也不觸發狂怒。
            var s = Capture19Kit.NewActive19();
            for (int t = 1; t <= 13; t++) Step(s, 0, Far);
            s.SeedScoresForTest(0, 100);
            Step(s, 0, Far); // t14
            Assert.AreEqual(Neutral, s.OwnerOf(0), "S1b t14 0 號中立");
            Assert.AreEqual(1, s.BlueNeutralizedCount, "活性：S1b t14 孤島確實被中立化");
            Assert.AreEqual(0f, s.BlueRageRemaining, "S1b t14 藍方狂怒");
        }

        // ── V9-A14 狂怒清除 ──
        [Test]
        public void V9A14_EndedClearsRage_AndTheNextMatchStartsAtZero()
        {
            var l = Capture19Kit.NewSeeded19(S3Blue, S3Red);
            for (int t = 1; t <= 16; t++)
            {
                Step(l, S3Hero(t), S3Opponent(t));
                if (t == 15) l.SeedScoresForTest(0, 100);
            }
            Assert.AreEqual(12.0f, l.BlueRageRemaining, "t16 藍方狂怒");
            Step(l, S3Hero(17), S3Opponent(17));
            l.SeedScoresForTest(0, 998);
            for (int t = 18; t <= 20; t++) Step(l, S3Hero(t), S3Opponent(t));
            Assert.AreEqual(CaptureMatchState.Ended, l.State, "t20 Ended");
            Assert.AreEqual(CaptureMatchResult.RedWins, l.Result, "t20 紅勝");
            Assert.AreEqual(1016, l.RedScore, "t20 紅分");
            Assert.AreEqual(0f, l.BlueRageRemaining, "t20 藍方狂怒");
            Assert.AreEqual(0f, l.RedRageRemaining, "t20 紅方狂怒");
            for (int t = 21; t <= 31; t++)
            {
                Step(l, S3Hero(t), S3Opponent(t));
                Assert.AreEqual(CaptureMatchState.Ended, l.State, "t" + t + " 結算停頓中");
                Assert.AreEqual(0f, l.BlueRageRemaining, "t" + t + " 藍方狂怒");
                Assert.AreEqual(0f, l.RedRageRemaining, "t" + t + " 紅方狂怒");
            }
            Step(l, Far, Far); // t32：回 Lobby
            Assert.AreEqual(CaptureMatchState.Lobby, l.State, "t32 回 Lobby");
            Assert.IsTrue(l.TryStart());
            Assert.AreEqual(0f, l.BlueRageRemaining, "下一局藍方狂怒");
            Assert.AreEqual(0f, l.RedRageRemaining, "下一局紅方狂怒");
        }
    }
}
