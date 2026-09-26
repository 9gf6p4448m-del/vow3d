using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // V10-A08～A10、A13、A15：慢計分、局長量級、15 分鐘倒數、純邏輯零配置、舊夾具行為不變
    // （V0100_SANCTUARY_PLAN.md §3-A，凍結；期望值一律寫死字面值；dt＝0.25）。
    public sealed class CaptureSanctuaryScoringTests
    {
        private const int Blue = CaptureSanctuaryKit.Blue;
        private const int Red = CaptureSanctuaryKit.Red;

        // V9-A07／S2 盤面（Capture19EncircleTests 的 S2Blue/S2Red 是 private，這裡照字面值另存一份）。
        private static readonly int[] RageBoardBlue = { 12, 13, 14, 4, 0 };
        private static readonly int[] RageBoardRed = { 7, 8, 18, 1, 2, 3 };

        // V10-A06／A07 共用主盤面，此檔也要用（A13、A15）。
        private static readonly int[] MainBlue = { 12, 13, 14, 4 };
        private static readonly int[] MainRed = { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18 };

        // ── V10-A08 慢計分 ──
        [Test]
        public void V10A08_PacedScoring()
        {
            // 開局盤面、雙方在遠處：每塊每秒 1/7 分，帶整數餘數。
            var main = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 4; t++) main.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(0, main.BlueScore, "t4 藍分"); Assert.AreEqual(0, main.RedScore, "t4 紅分");
            for (int t = 5; t <= 12; t++) main.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(1, main.BlueScore, "t12 藍分"); Assert.AreEqual(1, main.RedScore, "t12 紅分");
            for (int t = 13; t <= 28; t++) main.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(3, main.BlueScore, "t28 藍分"); Assert.AreEqual(3, main.RedScore, "t28 紅分");
            Assert.AreEqual(7, main.ScoreTickCount, "活性：t28 ScoreTickCount");

            // 種子清餘數：不清的話第 12 個 tick 就會結束。
            var seedClear = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 6; t++) seedClear.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            seedClear.SeedScoresForTest(999, 999);
            for (int t = 7; t <= 12; t++) seedClear.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(999, seedClear.BlueScore, "種子清餘數 t12 藍分仍 999");
            Assert.AreEqual(999, seedClear.RedScore, "種子清餘數 t12 紅分仍 999");
            Assert.AreEqual(CaptureMatchState.Active, seedClear.State, "種子清餘數 t12 仍 Active");
            for (int t = 13; t <= 16; t++) seedClear.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Ended, seedClear.State, "活性：t16 Ended");
            Assert.AreEqual(CaptureMatchResult.Draw, seedClear.Result, "t16 Draw");
            Assert.AreEqual(1000, seedClear.BlueScore); Assert.AreEqual(1000, seedClear.RedScore);

            // 種子 (999,998)：t16 BlueWins、1000／999。
            var altSeed = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 6; t++) altSeed.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            altSeed.SeedScoresForTest(999, 998);
            for (int t = 7; t <= 16; t++) altSeed.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchResult.BlueWins, altSeed.Result, "altSeed t16 BlueWins");
            Assert.AreEqual(1000, altSeed.BlueScore); Assert.AreEqual(999, altSeed.RedScore);

            // 19 塊全藍：第 4 個 tick 後藍 2（19→2 餘 5）、第 8 個 tick 後藍 5（24→3 餘 3）。
            var allBlueOwnership = new int[19];
            for (int i = 0; i < 19; i++) allBlueOwnership[i] = i;
            var allBlue = CaptureSanctuaryKit.NewSeeded0100(allBlueOwnership, new int[0]);
            for (int t = 1; t <= 4; t++) allBlue.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(2, allBlue.BlueScore, "19 塊全藍 t4");
            for (int t = 5; t <= 8; t++) allBlue.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(5, allBlue.BlueScore, "19 塊全藍 t8");

            // 夾具：V090Nineteen 開局第 4 個 tick 後 6／6（舊制每塊每秒 2 分，3 塊直接乘）。
            var fixture = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(fixture.TryEnterCaptureMode());
            Assert.IsTrue(fixture.TryStart());
            for (int t = 1; t <= 4; t++) fixture.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(6, fixture.BlueScore, "夾具 t4 藍分"); Assert.AreEqual(6, fixture.RedScore, "夾具 t4 紅分");

            // 長程：開局盤面，第 364 個 tick 後 39／39（3 塊×91 次計分＝273 單位＝39 分整，純整數不會有 float 誤差）。
            var longRange = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 364; t++) longRange.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(39, longRange.BlueScore, "長程 t364 藍分");
            Assert.AreEqual(39, longRange.RedScore, "長程 t364 紅分");

            // (i) 落後判定用整數分數：種子 (83,98)，第 16 個 tick 恰好 15% 不觸發；種子 (82,98) 超過 15% 觸發。
            var i1 = CaptureSanctuaryKit.NewSeeded0100(RageBoardBlue, RageBoardRed);
            for (int t = 1; t <= 2; t++) i1.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            i1.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t3
            i1.SeedScoresForTest(83, 98);
            for (int t = 4; t <= 15; t++) i1.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4));
            Assert.AreEqual(85, i1.BlueScore, "(i-83) t15 藍分"); Assert.AreEqual(100, i1.RedScore, "(i-83) t15 紅分");
            i1.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t16
            Assert.AreEqual(1, i1.BlueCutEventCount, "活性：(i-83) t16 斷能");
            Assert.AreEqual(0f, i1.BlueRageRemaining, "(i-83) t16 藍方狂怒 0（恰好 15%）");
            Assert.AreEqual(85, i1.BlueScore, "(i-83) t16 藍分"); Assert.AreEqual(101, i1.RedScore, "(i-83) t16 紅分");

            var i2 = CaptureSanctuaryKit.NewSeeded0100(RageBoardBlue, RageBoardRed);
            for (int t = 1; t <= 2; t++) i2.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            i2.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t3
            i2.SeedScoresForTest(82, 98);
            for (int t = 4; t <= 15; t++) i2.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4));
            Assert.AreEqual(84, i2.BlueScore, "(i-82) t15 藍分"); Assert.AreEqual(100, i2.RedScore, "(i-82) t15 紅分");
            i2.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t16
            Assert.AreEqual(1, i2.BlueCutEventCount, "活性：(i-82) t16 斷能");
            Assert.AreEqual(12.0f, i2.BlueRageRemaining, "(i-82) t16 藍方狂怒 12.0（超過 15%）");
            Assert.AreEqual(84, i2.BlueScore, "(i-82) t16 藍分"); Assert.AreEqual(101, i2.RedScore, "(i-82) t16 紅分");

            // (ii) 同盤面，較晚斷線：種子在 TryStart 後、第 1 個 tick 前。
            var ii = CaptureSanctuaryKit.NewSeeded0100(RageBoardBlue, RageBoardRed);
            ii.SeedScoresForTest(24, 28);
            for (int t = 1; t <= 46; t++) ii.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            for (int t = 47; t <= 59; t++) ii.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4));
            Assert.AreEqual(34, ii.BlueScore, "(ii) t59 藍分"); Assert.AreEqual(40, ii.RedScore, "(ii) t59 紅分");
            ii.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t60
            Assert.AreEqual(1, ii.BlueCutEventCount, "活性：(ii) t60 斷能");
            Assert.AreEqual(0f, ii.BlueRageRemaining, "(ii) t60 藍方狂怒 0（恰好 15%）");
            Assert.AreEqual(34, ii.BlueScore, "(ii) t60 藍分"); Assert.AreEqual(41, ii.RedScore, "(ii) t60 紅分");
        }

        // ── V10-A09 局長量級（放置局，純邏輯驅動器，不動被測物） ──
        [Test]
        public void V10A09_PlacementMatch_RedReachesAThousand_WithinExpectedWindow_AndSanctuaryDecaysAtLeastOnce()
        {
            var l = CaptureSanctuaryKit.NewActive0100();
            var spec = CaptureBoardSpec.V0100Sanctuary;
            float oppX = CaptureSanctuaryKit.RedSpawnX, oppZ = CaptureSanctuaryKit.RedSpawnZ;
            bool sanctuaryDecayed = false;
            int endTick = -1;
            var ownership = new int[19];
            for (int t = 1; t <= 2400; t++)
            {
                for (int i = 0; i < 19; i++) ownership[i] = l.OwnerOf(i);
                int target = CaptureOpponentPolicy.SelectTargetTile(oppX, oppZ, ownership, spec);
                if (target != -1)
                {
                    float tx = spec.CenterX(target), tz = spec.CenterZ(target);
                    float dx = tx - oppX, dz = tz - oppZ;
                    float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
                    const float step = 4.0f * 0.25f;
                    if (dist <= step) { oppX = tx; oppZ = tz; }
                    else { oppX += dx / dist * step; oppZ += dz / dist * step; }
                }
                l.Tick(0.25f, 1000f, 1000f, oppX, oppZ);
                if (l.BlueSanctuaryPercent < 15) sanctuaryDecayed = true;
                if (l.State == CaptureMatchState.Ended) { endTick = t; break; }
            }
            Assert.GreaterOrEqual(endTick, 1200, "結束 tick 下界（300 秒）");
            Assert.LessOrEqual(endTick, 2400, "結束 tick 上界（600 秒）");
            Assert.AreEqual(CaptureMatchResult.RedWins, l.Result, "紅方勝");
            Assert.IsFalse(l.EndedByTime, "非時間到結束");
            Assert.IsTrue(sanctuaryDecayed, "活性：藍方聖所強度結束前曾經 < 15");
        }

        // ── V10-A10 15 分鐘倒數 ──
        [Test]
        public void V10A10_FifteenMinuteCountdown()
        {
            // (a) 開局、雙方在遠處：時間到、Draw、385／385。
            var a = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 3599; t++) a.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Active, a.State, "(a) t3599 仍 Active");
            Assert.AreEqual(0.25f, a.MatchRemainingSeconds, 1e-6f, "(a) t3599 剩餘秒數");
            a.Tick(0.25f, 1000f, 1000f, 1000f, 1000f); // t3600
            Assert.AreEqual(CaptureMatchState.Ended, a.State, "活性：(a) t3600 Ended");
            Assert.IsTrue(a.EndedByTime, "(a) EndedByTime");
            Assert.AreEqual(CaptureMatchResult.Draw, a.Result, "(a) Draw");
            Assert.AreEqual(385, a.BlueScore, "(a) 藍分"); Assert.AreEqual(385, a.RedScore, "(a) 紅分");

            // (b) 種子藍{12,13,14,4}、紅{7,8,18}：BlueWins、514／385。
            var b = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14, 4 }, new[] { 7, 8, 18 });
            for (int t = 1; t <= 3600; t++) b.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchResult.BlueWins, b.Result, "(b) BlueWins");
            Assert.AreEqual(514, b.BlueScore, "(b) 藍分"); Assert.AreEqual(385, b.RedScore, "(b) 紅分");

            // (c) 鏡像：藍{12,13,14}、紅{7,8,18,1}：RedWins、385／514。
            var c = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14 }, new[] { 7, 8, 18, 1 });
            for (int t = 1; t <= 3600; t++) c.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchResult.RedWins, c.Result, "(c) RedWins");
            Assert.AreEqual(385, c.BlueScore, "(c) 藍分"); Assert.AreEqual(514, c.RedScore, "(c) 紅分");

            // (d) 倒地不暫停：同 (a)，第 100 個 tick 後 NotifyKnockedOut(藍)，仍在第 3600 個 tick 結束。
            var d = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 100; t++) d.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            d.NotifyKnockedOut(Blue);
            for (int t = 101; t <= 3600; t++) d.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Ended, d.State, "(d) 倒地不暫停仍第 3600 結束");
            Assert.IsTrue(d.EndedByTime, "(d) EndedByTime");

            // (e) 同 tick：第 3588 個 tick 後種子 (999,384) → 第 3600 個 tick：1000 分勝優先於時間到。
            var e = CaptureSanctuaryKit.NewActive0100();
            for (int t = 1; t <= 3588; t++) e.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            e.SeedScoresForTest(999, 384);
            for (int t = 3589; t <= 3600; t++) e.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Ended, e.State, "(e) Ended");
            Assert.AreEqual(CaptureMatchResult.BlueWins, e.Result, "(e) BlueWins");
            Assert.AreEqual(1000, e.BlueScore, "(e) 藍分"); Assert.AreEqual(385, e.RedScore, "(e) 紅分");
            Assert.IsFalse(e.EndedByTime, "活性：(e) EndedByTime 為 false（⑥ 判定，不是 ⑦）");

            // (f) 種子入口：TryStart 後 SeedMatchElapsedForTest(899.75) → 1 個 tick 後 Ended、EndedByTime。
            var f = CaptureSanctuaryKit.NewActive0100();
            f.SeedMatchElapsedForTest(899.75f);
            f.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Ended, f.State, "(f) Ended");
            Assert.IsTrue(f.EndedByTime, "(f) EndedByTime");

            // (g) 清狂怒：V9-A07 盤面與走位，第 16 個 tick 觸發狂怒，時間到清狂怒。
            var g = CaptureSanctuaryKit.NewSeeded0100(RageBoardBlue, RageBoardRed);
            for (int t = 1; t <= 2; t++) g.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            g.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t3
            for (int t = 4; t <= 15; t++) g.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4));
            g.SeedScoresForTest(0, 100);
            g.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4)); // t16
            Assert.AreEqual(12.0f, g.BlueRageRemaining, "(g) t16 藍方狂怒");
            g.SeedMatchElapsedForTest(899.0f);
            for (int t = 17; t <= 20; t++) g.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(4), Capture19Kit.Z(4));
            Assert.AreEqual(CaptureMatchState.Ended, g.State, "(g) Ended");
            Assert.IsTrue(g.EndedByTime, "(g) EndedByTime");
            Assert.AreEqual(0f, g.BlueRageRemaining, "活性：(g) 時間到清藍方狂怒");
            Assert.AreEqual(0f, g.RedRageRemaining, "(g) 時間到清紅方狂怒");

            // (h) 第二局：回 Lobby 後 TryStart → 全部歸零。
            var h = CaptureSanctuaryKit.NewActive0100();
            h.SeedScoresForTest(999, 0);
            for (int t = 1; t <= 12; t++) h.Tick(0.25f, 1000f, 1000f, 1000f, 1000f); // 3 次計分湊滿 7 單位 → 1000 分
            Assert.AreEqual(CaptureMatchState.Ended, h.State, "(h) 活性：先結束一局");
            Assert.AreEqual(CaptureMatchResult.BlueWins, h.Result, "(h) 活性：BlueWins");
            for (int t = 1; t <= 12; t++) h.Tick(0.25f, 1000f, 1000f, 1000f, 1000f); // 結算 3 秒＝12 個 tick
            Assert.AreEqual(CaptureMatchState.Lobby, h.State, "活性：(h) 回 Lobby");
            Assert.IsTrue(h.TryStart());
            Assert.AreEqual(900f, h.MatchRemainingSeconds, "(h) 第二局 MatchRemainingSeconds");
            Assert.IsFalse(h.EndedByTime, "(h) 第二局 EndedByTime");
            Assert.AreEqual(15, h.BlueSanctuaryPercent, "(h) 第二局藍方 pct");
            Assert.AreEqual(15, h.RedSanctuaryPercent, "(h) 第二局紅方 pct");
            Assert.AreEqual(0f, h.BlueSiegeSeconds, "(h) 第二局藍方 siege");
            Assert.AreEqual(0f, h.RedSiegeSeconds, "(h) 第二局紅方 siege");

            // (i) 夾具：V090Nineteen 在 SeedMatchElapsedForTest(899.75) 後跑 1 個 tick 仍是 Active。
            var i = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(i.TryEnterCaptureMode());
            Assert.IsTrue(i.TryStart());
            i.SeedMatchElapsedForTest(899.75f);
            i.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Active, i.State, "(i) 夾具仍 Active");
        }

        // ── V10-A13 純邏輯零配置（只在 dotnet 有鑑別力；Unity 端標 Ignore） ──
        [Test]
#if UNITY_5_3_OR_NEWER
        [Ignore("dotnet only: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono; Unity-side coverage is the PlayMode profiler probe")]
#endif
        public void V10A13_SanctuarySiegeAndCountdown_AllocateZeroBytes()
        {
            var logic = CaptureSanctuaryKit.NewSeeded0100(MainBlue, MainRed);

            long before = System.GC.GetAllocatedBytesForCurrentThread();

            int previousPct = 15;
            int decaySteps = 0;
            bool sawInSanctuary = false, sawNotInSanctuary = false;
            for (int t = 1; t <= 1000; t++)
            {
                float heroX = t <= 600 ? CaptureSanctuaryKit.BlueSpawnX : Capture19Kit.X(4);
                float heroZ = t <= 600 ? CaptureSanctuaryKit.BlueSpawnZ : Capture19Kit.Z(4);
                logic.Tick(0.25f, heroX, heroZ, Capture19Kit.X(11), Capture19Kit.Z(11));

                if (logic.BlueSanctuaryPercent < previousPct) decaySteps++;
                previousPct = logic.BlueSanctuaryPercent;
                if (logic.BlueInSanctuary) sawInSanctuary = true; else sawNotInSanctuary = true;
            }

            long after = System.GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(before, after, "1000 個 tick（含聖所、圍城、翻塊）不得配置任何位元組");
            Assert.AreEqual(CaptureMatchState.Active, logic.State, "活性：仍在對局中");
            Assert.GreaterOrEqual(decaySteps, 15, "活性：圍城衰減步數");
            Assert.GreaterOrEqual(logic.FlipCount, 1, "活性：翻塊");
            Assert.GreaterOrEqual(logic.ScoreTickCount, 250, "活性：計分");
            Assert.IsTrue(sawInSanctuary, "活性：曾經在聖所");
            Assert.IsTrue(sawNotInSanctuary, "活性：曾經不在聖所");

            var timeLimitLogic = CaptureSanctuaryKit.NewActive0100();
            long beforeTime = System.GC.GetAllocatedBytesForCurrentThread();
            for (int t = 1; t <= 3600; t++) timeLimitLogic.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            long afterTime = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(beforeTime, afterTime, "3600 個 tick（時間到）不得配置任何位元組");
            Assert.IsTrue(timeLimitLogic.EndedByTime, "活性：時間到結束");
        }

        // ── V10-A15 夾具行為不變 ──
        [Test]
        public void V10A15_OldFixturesAreUnaffectedBySanctuaryAndSiege()
        {
            var v9 = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(v9.TryEnterCaptureMode());
            Assert.IsTrue(v9.TryStart());
            v9.Tick(0.25f, CaptureSanctuaryKit.BlueSpawnX, CaptureSanctuaryKit.BlueSpawnZ, 1000f, 1000f);
            Assert.AreEqual(100, v9.BlueDamageTakenPercent, "V090Nineteen 出生點仍 100");

            var v8 = new CaptureMatchLogic(new CaptureTuning());
            Assert.IsTrue(v8.TryEnterCaptureMode());
            Assert.IsTrue(v8.TryStart());
            v8.Tick(0.25f, 0f, -13.625f, 1000f, 1000f); // v0.8.0 藍方出生點字面值（CaptureTuning.BlueHomeRespawnZ）
            Assert.AreEqual(100, v8.BlueDamageTakenPercent, "V080Seven 出生點仍 100");

            var main = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(main.TryEnterCaptureMode());
            Assert.IsTrue(main.TryStart());
            main.SeedOwnershipForTest(Capture19Kit.Board(MainBlue, MainRed));
            for (int t = 1; t <= 140; t++) main.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Active, main.State, "夾具 t140 仍 Active（紅方 14 塊×2＝28 分/秒，35 秒 980 分）");
            Assert.AreEqual(980, main.RedScore, "夾具 t140 紅分");
            Assert.AreEqual(15, main.BlueSanctuaryPercent, "夾具不開圍城，pct 恆 15");

            var siegeSeed = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(siegeSeed.TryEnterCaptureMode());
            Assert.IsTrue(siegeSeed.TryStart());
            siegeSeed.SeedOwnershipForTest(Capture19Kit.Board(MainBlue, MainRed));
            siegeSeed.SeedSiegeForTest(Blue, 119.5f);
            for (int t = 1; t <= 8; t++)
            {
                siegeSeed.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
                Assert.AreEqual(15, siegeSeed.BlueSanctuaryPercent, "t" + t + " 夾具即使種子逼近門檻仍不衰減");
            }
        }
    }
}
