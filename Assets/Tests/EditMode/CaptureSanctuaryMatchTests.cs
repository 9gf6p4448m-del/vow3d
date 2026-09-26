using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    // v0.10.0 步驟 A 共用的字面資料與走位工具（V0100_SANCTUARY_PLAN.md §3-A）。沿用 Capture19Kit 的座標表
    // （V0100Sanctuary 與 V090Nineteen 幾何逐值相同，V10-A02 守）；只加 V0100Sanctuary 專用的建構/種子入口。
    internal static class CaptureSanctuaryKit
    {
        public const int Blue = Capture19Kit.Blue;
        public const int Red = Capture19Kit.Red;
        public const int Far = Capture19Kit.Far;

        public static CaptureMatchLogic NewActive0100()
        {
            var logic = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0100Sanctuary);
            Assert.IsTrue(logic.TryEnterCaptureMode());
            Assert.IsTrue(logic.TryStart());
            return logic;
        }

        public static CaptureMatchLogic NewSeeded0100(int[] blue, int[] red)
        {
            var logic = NewActive0100();
            logic.SeedOwnershipForTest(Capture19Kit.Board(blue, red));
            return logic;
        }

        // 出生點（E20 字面座標）。
        public const float BlueSpawnX = 0f, BlueSpawnZ = -16.65625f;
        public const float RedSpawnX = 0f, RedSpawnZ = 16.65625f;

        public static void Step(CaptureMatchLogic logic, int heroTile, int opponentTile)
        {
            logic.Tick(Capture19Kit.Dt, Capture19Kit.X(heroTile), Capture19Kit.Z(heroTile),
                                        Capture19Kit.X(opponentTile), Capture19Kit.Z(opponentTile));
        }

        public static void StepAt(CaptureMatchLogic logic, float dt, float heroX, float heroZ, float oppX, float oppZ)
        {
            logic.Tick(dt, heroX, heroZ, oppX, oppZ);
        }
    }

    // V10-A03～A07：聖所在聖所判定／受傷百分比、同 tick 歸屬順序、奪回 1.8 秒、圍城衰減與恢復
    // （V0100_SANCTUARY_PLAN.md §3-A，凍結；期望值一律寫死字面值；純邏輯時序 dt＝0.25，唯一例外 A05 用 dt＝1/64）。
    public sealed class CaptureSanctuaryMatchTests
    {
        private const int Blue = CaptureSanctuaryKit.Blue;
        private const int Red = CaptureSanctuaryKit.Red;
        private const int Far = CaptureSanctuaryKit.Far;

        // ── V10-A03 在聖所與受傷百分比 ──
        [Test]
        public void V10A03_InSanctuary_AndDamageTakenPercent()
        {
            // 英雄在出生點（預設開局盤面：藍{12,13,14}、紅{7,8,18}）。
            var l1 = CaptureSanctuaryKit.NewActive0100();
            l1.Tick(0.25f, CaptureSanctuaryKit.BlueSpawnX, CaptureSanctuaryKit.BlueSpawnZ, 1000f, 1000f);
            Assert.IsTrue(l1.BlueInSanctuary, "出生點在聖所");
            Assert.AreEqual(85, l1.BlueDamageTakenPercent, "出生點受傷百分比");

            // (0,-11.375)：仍在 13 號板塊內（InRadius=3.7890625 > 3.78125），光圈外（半徑 2.5 < 3.78125）。
            var l2 = CaptureSanctuaryKit.NewActive0100();
            l2.Tick(0.25f, 0f, -11.375f, 1000f, 1000f);
            Assert.AreEqual(85, l2.BlueDamageTakenPercent, "(0,-11.375) 仍在 13 號板塊內");

            // (0,-11.3671875)：4/13 共用邊，重疊取索引小 → 歸 4 號（不是藍方母板塊）。
            var l3 = CaptureSanctuaryKit.NewActive0100();
            l3.Tick(0.25f, 0f, -11.3671875f, 1000f, 1000f);
            Assert.AreEqual(100, l3.BlueDamageTakenPercent, "共用邊歸 4 號");

            // (0,-12.0)：13 號板塊內、光圈外（用 TileAt 不是 CircleAt 才抓得到）。
            var l4 = CaptureSanctuaryKit.NewActive0100();
            l4.Tick(0.25f, 0f, -12.0f, 1000f, 1000f);
            Assert.AreEqual(85, l4.BlueDamageTakenPercent, "板塊內光圈外仍算聖所");

            // 塔心 12（藍方母板塊，開局即持有）。
            var l5 = CaptureSanctuaryKit.NewActive0100();
            l5.Tick(0.25f, Capture19Kit.X(12), Capture19Kit.Z(12), 1000f, 1000f);
            Assert.AreEqual(85, l5.BlueDamageTakenPercent, "塔心 12");

            // 藍{12,13,14,4} 時塔心 4：藍方持有但不是藍方自己的母板塊 → 100。
            var l6 = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14, 4 }, new[] { 7, 8, 18 });
            l6.Tick(0.25f, Capture19Kit.X(4), Capture19Kit.Z(4), 1000f, 1000f);
            Assert.AreEqual(100, l6.BlueDamageTakenPercent, "持有但非自己母板塊");

            // 紅{7,8,18,1,0,4,13}、藍{12,14} 時出生點：13 號是藍方母板塊但不在藍方手上 → 100。
            var l7 = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 });
            l7.Tick(0.25f, CaptureSanctuaryKit.BlueSpawnX, CaptureSanctuaryKit.BlueSpawnZ, 1000f, 1000f);
            Assert.AreEqual(100, l7.BlueDamageTakenPercent, "自己母板塊但不在自己手上");

            // 藍{12,13,14,4,0,1,7}、紅{18,8} 時塔心 7：只算自己的母板塊，7 號是紅方的 → 100。
            var l8 = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14, 4, 0, 1, 7 }, new[] { 18, 8 });
            l8.Tick(0.25f, Capture19Kit.X(7), Capture19Kit.Z(7), 1000f, 1000f);
            Assert.AreEqual(100, l8.BlueDamageTakenPercent, "只算自己的母板塊");

            // 對手在出生點。
            var l9 = CaptureSanctuaryKit.NewActive0100();
            l9.Tick(0.25f, 1000f, 1000f, CaptureSanctuaryKit.RedSpawnX, CaptureSanctuaryKit.RedSpawnZ);
            Assert.AreEqual(85, l9.RedDamageTakenPercent, "對手出生點");

            // 對手在塔心 13（藍方持有）：不是紅方自己的母板塊 → 100。
            var l10 = CaptureSanctuaryKit.NewActive0100();
            l10.Tick(0.25f, 1000f, 1000f, Capture19Kit.X(13), Capture19Kit.Z(13));
            Assert.AreEqual(100, l10.RedDamageTakenPercent, "對手站在藍方母板塊上");

            // NotifyKnockedOut 後下一 tick：倒地不算在聖所。
            var l11 = CaptureSanctuaryKit.NewActive0100();
            l11.Tick(0.25f, CaptureSanctuaryKit.BlueSpawnX, CaptureSanctuaryKit.BlueSpawnZ, 1000f, 1000f);
            Assert.IsTrue(l11.BlueInSanctuary, "倒地前在聖所");
            l11.NotifyKnockedOut(Blue);
            l11.Tick(0.25f, CaptureSanctuaryKit.BlueSpawnX, CaptureSanctuaryKit.BlueSpawnZ, 1000f, 1000f);
            Assert.IsFalse(l11.BlueInSanctuary, "倒地後不算聖所");
            Assert.AreEqual(100, l11.BlueDamageTakenPercent, "倒地後受傷百分比 100");

            // Lobby（TryStart 之前）與 Ended 之後雙方都是 100。
            var l12 = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0100Sanctuary);
            Assert.IsTrue(l12.TryEnterCaptureMode());
            Assert.AreEqual(100, l12.BlueDamageTakenPercent, "Lobby 藍方 100");
            Assert.AreEqual(100, l12.RedDamageTakenPercent, "Lobby 紅方 100");
            Assert.IsTrue(l12.TryStart());
            l12.SeedScoresForTest(999, 0);
            // 3 塊×3 次計分＝9 單位＝1 分整，999→1000（12 個 tick＝3 次計分）。
            for (int t = 1; t <= 12; t++)
                l12.Tick(0.25f, CaptureSanctuaryKit.BlueSpawnX, CaptureSanctuaryKit.BlueSpawnZ, 1000f, 1000f);
            Assert.AreEqual(CaptureMatchState.Ended, l12.State, "活性：已結束");
            Assert.AreEqual(100, l12.BlueDamageTakenPercent, "Ended 藍方 100");
            Assert.AreEqual(100, l12.RedDamageTakenPercent, "Ended 紅方 100");
        }

        // ── V10-A04 同 tick 失去母板塊：③c 用 ③b 之後的歸屬，不是 ③ 之前的 ──
        [Test]
        public void V10A04_SanctuaryUsesOwnershipAfterEncircle_NotBeforeChannels()
        {
            var l = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14 }, new[] { 7, 8, 18, 1, 0, 4 });
            for (int t = 1; t <= 13; t++) l.Tick(0.25f, 0f, -12.0f, Capture19Kit.X(13), Capture19Kit.Z(13));
            Assert.AreEqual(Blue, l.OwnerOf(13), "t13 13 號仍藍");
            Assert.AreEqual(85, l.BlueDamageTakenPercent, "t13 仍在聖所");

            l.Tick(0.25f, 0f, -12.0f, Capture19Kit.X(13), Capture19Kit.Z(13)); // t14
            Assert.AreEqual(Red, l.OwnerOf(13), "活性：t14 13 號翻紅");
            Assert.AreEqual(100, l.BlueDamageTakenPercent, "t14 用翻塊後的歸屬，不再是聖所");
        }

        // ── V10-A05 奪回 1.8 秒（dt＝1/64，§3 共同遵守的唯一例外） ──
        [Test]
        public void V10A05_ReclaimOwnMotherTile_TakesOneEightSeconds_AtSixtyFourthTickDelta()
        {
            const float dt = 1f / 64f;
            float t13x = Capture19Kit.X(13), t13z = Capture19Kit.Z(13);
            float t4x = Capture19Kit.X(4), t4z = Capture19Kit.Z(4);
            float t7x = Capture19Kit.X(7), t7z = Capture19Kit.Z(7);
            float farX = Capture19Kit.X(Far), farZ = Capture19Kit.Z(Far);

            // (a) 紅{7,8,18,1,0,4,13}、藍{12,14}；英雄在塔心 13、對手在遠處。
            var a = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 });
            for (int t = 1; t <= 115; t++) a.Tick(dt, t13x, t13z, farX, farZ);
            Assert.AreEqual(Red, a.OwnerOf(13), "(a) t115 仍紅");
            Assert.AreEqual(1.796875f, a.BlueChannelProgress, 1e-6f, "(a) t115 進度");
            Assert.AreEqual(1.8f, a.BlueChannelRequiredSeconds, "(a) t115 門檻 1.8");
            a.Tick(dt, t13x, t13z, farX, farZ); // t116
            Assert.AreEqual(Blue, a.OwnerOf(13), "活性：(a) t116 翻藍");
            Assert.AreEqual(0, a.BlueNeutralizedCount, "(a) t116 藍方中立化數不變");

            // (b) 同上但 13 號中立（不是敵方持有也一樣快）。
            var b = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4 });
            for (int t = 1; t <= 115; t++) b.Tick(dt, t13x, t13z, farX, farZ);
            Assert.AreNotEqual(Blue, b.OwnerOf(13), "(b) t115 尚未翻");
            b.Tick(dt, t13x, t13z, farX, farZ); // t116
            Assert.AreEqual(Blue, b.OwnerOf(13), "活性：(b) t116 翻藍（中立也算奪回）");

            // (c) 對照：紅{7,8,18,1,0}、藍{12,13,14}，英雄在塔心 4（非母板塊）：門檻仍是 3.5。
            var c = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14 }, new[] { 7, 8, 18, 1, 0 });
            for (int t = 1; t <= 223; t++) c.Tick(dt, t4x, t4z, farX, farZ);
            Assert.AreEqual(3.5f, c.BlueChannelRequiredSeconds, "(c) t223 門檻 3.5");
            Assert.AreNotEqual(Blue, c.OwnerOf(4), "(c) t223 尚未翻");
            c.Tick(dt, t4x, t4z, farX, farZ); // t224
            Assert.AreEqual(Blue, c.OwnerOf(4), "活性：(c) t224 翻藍");

            // (d) 紅方：藍{12,13,14,4,0,1,7}、紅{18,8}，對手在塔心 7（紅方自己的母板塊）：同樣 1.8 秒。
            var d = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 13, 14, 4, 0, 1, 7 }, new[] { 18, 8 });
            for (int t = 1; t <= 115; t++) d.Tick(dt, farX, farZ, t7x, t7z);
            Assert.AreNotEqual(Red, d.OwnerOf(7), "(d) t115 尚未翻");
            d.Tick(dt, farX, farZ, t7x, t7z); // t116
            Assert.AreEqual(Red, d.OwnerOf(7), "活性：(d) t116 翻紅");

            // (e) 打斷：同 (a)，第 100 個 tick 後 NotifyDamaged(藍) → 從頭起算，第 215 仍紅、第 216 翻藍。
            var e = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 });
            for (int t = 1; t <= 100; t++) e.Tick(dt, t13x, t13z, farX, farZ);
            e.NotifyDamaged(Blue);
            for (int t = 101; t <= 215; t++) e.Tick(dt, t13x, t13z, farX, farZ);
            Assert.AreEqual(Red, e.OwnerOf(13), "(e) t215 仍紅");
            e.Tick(dt, t13x, t13z, farX, farZ); // t216
            Assert.AreEqual(Blue, e.OwnerOf(13), "活性：(e) t216 翻藍");

            // (f) dt＝0.25：同 (a) 盤面，第 7 個 tick 仍紅、第 8 個 tick 翻藍。
            var f = CaptureSanctuaryKit.NewSeeded0100(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 });
            for (int t = 1; t <= 7; t++) f.Tick(0.25f, t13x, t13z, farX, farZ);
            Assert.AreEqual(Red, f.OwnerOf(13), "(f) t7 仍紅");
            f.Tick(0.25f, t13x, t13z, farX, farZ); // t8
            Assert.AreEqual(Blue, f.OwnerOf(13), "活性：(f) t8 翻藍");

            // (g) 夾具：V090Nineteen 同 (a) 盤面 → 門檻不縮短，仍是 3.5（第 223 仍紅、第 224 翻藍）。
            var g = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V090Nineteen);
            Assert.IsTrue(g.TryEnterCaptureMode());
            Assert.IsTrue(g.TryStart());
            g.SeedOwnershipForTest(Capture19Kit.Board(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 }));
            for (int t = 1; t <= 223; t++) g.Tick(dt, t13x, t13z, farX, farZ);
            Assert.AreEqual(Red, g.OwnerOf(13), "(g) t223 仍紅（夾具不縮短）");
            g.Tick(dt, t13x, t13z, farX, farZ); // t224
            Assert.AreEqual(Blue, g.OwnerOf(13), "活性：(g) t224 翻藍");
        }

        // V10-A06／A07 共用主盤面：藍{12,13,14,4}（4 塊）、紅＝其餘去掉 11 號（14 塊）。
        private static readonly int[] MainBlue = { 12, 13, 14, 4 };
        private static readonly int[] MainRed = { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18 };

        // ── V10-A06 圍城門檻與衰減時序 ──
        [Test]
        public void V10A06_SiegeThresholdAndDecayTiming()
        {
            var l = CaptureSanctuaryKit.NewSeeded0100(MainBlue, MainRed);
            var checkpoints = new System.Collections.Generic.Dictionary<int, int>
            {
                { 479, 15 }, { 480, 15 }, { 483, 15 }, { 484, 14 }, { 487, 14 }, { 488, 13 },
                { 539, 1 }, { 540, 0 }, { 544, 0 },
            };
            int maxTick = 544;
            for (int t = 1; t <= maxTick; t++)
            {
                l.Tick(0.25f, t >= 520 ? CaptureSanctuaryKit.BlueSpawnX : 1000f, t >= 520 ? CaptureSanctuaryKit.BlueSpawnZ : 1000f, 1000f, 1000f);
                if (checkpoints.TryGetValue(t, out int expected))
                    Assert.AreEqual(expected, l.BlueSanctuaryPercent, "t" + t + " BlueSanctuaryPercent");
                Assert.AreEqual(15, l.RedSanctuaryPercent, "t" + t + " RedSanctuaryPercent 全程 15");
                if (t == 520) Assert.AreEqual(95, l.BlueDamageTakenPercent, "t520 受傷百分比套用衰減後的 pct");
            }

            // 對照：13 塊（0 號改中立），門檻不到，跑 600 個 tick 恆 15。
            var control = CaptureSanctuaryKit.NewSeeded0100(MainBlue, new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18 });
            for (int t = 1; t <= 600; t++)
            {
                control.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
                Assert.AreEqual(15, control.BlueSanctuaryPercent, "對照 t" + t + " 恆 15（13 塊不觸發）");
            }

            // 鏡像：藍持有 14 塊（去掉 1,7,8,17,18），紅{7,8,18,1} → 紅方被圍，第 484 個 tick 後 14。
            var mirrorBlue = new[] { 0, 2, 3, 4, 5, 6, 9, 10, 11, 12, 13, 14, 15, 16 };
            var mirror = CaptureSanctuaryKit.NewSeeded0100(mirrorBlue, new[] { 7, 8, 18, 1 });
            for (int t = 1; t <= 484; t++) mirror.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(14, mirror.RedSanctuaryPercent, "鏡像 t484 RedSanctuaryPercent");

            // 倒地不影響圍城：主盤面第 100 個 tick 後 NotifyKnockedOut(藍)，第 484 個 tick 後仍是 14。
            var ko = CaptureSanctuaryKit.NewSeeded0100(MainBlue, MainRed);
            for (int t = 1; t <= 100; t++) ko.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            ko.NotifyKnockedOut(Blue);
            for (int t = 101; t <= 484; t++) ko.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(14, ko.BlueSanctuaryPercent, "倒地不影響圍城 t484");
        }

        // ── V10-A07 圍城中斷、恢復與重新計時 ──
        [Test]
        public void V10A07_SiegeInterruptionRecoveryAndRetiming()
        {
            var l = CaptureSanctuaryKit.NewSeeded0100(MainBlue, MainRed);
            for (int t = 1; t <= 520; t++) l.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
            Assert.AreEqual(5, l.BlueSanctuaryPercent, "t520 pct 5（延續 A06）");

            // 0 號改中立：紅方剩 13 塊，條件解除。
            var redThirteen = new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18 };
            l.SeedOwnershipForTest(Capture19Kit.Board(MainBlue, redThirteen));

            var checkpoints = new System.Collections.Generic.Dictionary<int, int>
            {
                { 521, 5 }, { 524, 5 }, { 525, 6 }, { 529, 7 }, { 560, 14 }, { 561, 15 }, { 600, 15 },
            };
            for (int t = 521; t <= 600; t++)
            {
                l.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
                if (t == 521) Assert.AreEqual(0f, l.BlueSiegeSeconds, "t521 siege 歸零");
                if (checkpoints.TryGetValue(t, out int expected))
                    Assert.AreEqual(expected, l.BlueSanctuaryPercent, "t" + t + " 恢復中");
            }

            // 第 600 個 tick 後把 0 號改回紅（14 塊）：重新連續 120 秒才再開始減。
            l.SeedOwnershipForTest(Capture19Kit.Board(MainBlue, MainRed));
            var checkpoints2 = new System.Collections.Generic.Dictionary<int, int>
            {
                { 1080, 15 }, { 1083, 15 }, { 1084, 14 },
            };
            for (int t = 601; t <= 1084; t++)
            {
                l.Tick(0.25f, 1000f, 1000f, 1000f, 1000f);
                if (checkpoints2.TryGetValue(t, out int expected))
                    Assert.AreEqual(expected, l.BlueSanctuaryPercent, "t" + t + " 重新計時後");
            }
        }
    }
}
