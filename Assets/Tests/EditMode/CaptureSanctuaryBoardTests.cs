using NUnit.Framework;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests.EditMode
{
    // V10-A01、A02、A11、A12：新數值、規則集（與 V090Nineteen 逐值相等＋6 旗標）、HUD 字串表（Clock／Sanctuary／
    // Siege）、HUD 版面（MatchClock／SanctuaryRow）（V0100_SANCTUARY_PLAN.md §3-A，凍結；期望值一律寫死字面值）。
    public sealed class CaptureSanctuaryBoardTests
    {
        // ── V10-A01 新數值 ──
        [Test]
        public void V10A01_CaptureTuning_HasTheFrozenSanctuaryValues()
        {
            var t = new CaptureTuning();
            Assert.AreEqual(15, t.SanctuaryPercent);
            Assert.AreEqual(1.8f, t.ReclaimCaptureSeconds);
            Assert.AreEqual(7, t.SiegeTileNumerator);
            Assert.AreEqual(10, t.SiegeTileDenominator);
            Assert.AreEqual(120f, t.SiegeSeconds);
            Assert.AreEqual(1f, t.SiegeStepSeconds);
            Assert.AreEqual(900f, t.MatchTimeLimitSeconds);
            Assert.AreEqual(1, t.PacedScoreUnitsPerTilePerTick);
            Assert.AreEqual(7, t.PacedScoreUnitsPerPoint);
        }

        // ── V10-A02 規則集：與 V090Nineteen 逐值相等（容差 0），6 旗標全 true；舊夾具 4 個新旗標全 false ──
        [Test]
        public void V10A02_SanctuarySpec_MatchesNineteenGeometry_SixFlagsTrue_AndOldFixturesKeepThemFalse()
        {
            var v10 = CaptureBoardSpec.V0100Sanctuary;
            var v9 = CaptureBoardSpec.V090Nineteen;
            Assert.AreEqual(19, v10.TileCount);
            Assert.AreEqual(v9.CircumRadius, v10.CircumRadius, "外接半徑");
            Assert.AreEqual(v9.InRadius, v10.InRadius, "內切半徑");

            for (int i = 0; i < 19; i++)
            {
                Assert.AreEqual(v9.CenterX(i), v10.CenterX(i), "塔心 x " + i);
                Assert.AreEqual(v9.CenterZ(i), v10.CenterZ(i), "塔心 z " + i);
                Assert.AreEqual(v9.NeighborCount(i), v10.NeighborCount(i), "鄰居數 " + i);
                var a = new int[v9.NeighborCount(i)];
                var b = new int[v10.NeighborCount(i)];
                for (int k = 0; k < a.Length; k++) { a[k] = v9.Neighbor(i, k); b[k] = v10.Neighbor(i, k); }
                CollectionAssert.AreEqual(a, b, "鄰居 " + i);
            }

            for (int side = CaptureMatchLogic.BlueFactionId; side <= CaptureMatchLogic.RedFactionId; side++)
            {
                Assert.AreEqual(v9.MotherCount(side), v10.MotherCount(side), "母板塊數 " + side);
                for (int r = 0; r < v9.MotherCount(side); r++)
                {
                    Assert.AreEqual(v9.MotherTile(side, r), v10.MotherTile(side, r), "母板塊 " + side + "/" + r);
                    Assert.AreEqual(v9.MotherRespawnX(side, r), v10.MotherRespawnX(side, r), "復活點x " + side + "/" + r);
                    Assert.AreEqual(v9.MotherRespawnZ(side, r), v10.MotherRespawnZ(side, r), "復活點z " + side + "/" + r);
                }
                Assert.AreEqual(v9.EdgeRespawnX(side), v10.EdgeRespawnX(side), "場邊 x " + side);
                Assert.AreEqual(v9.EdgeRespawnZ(side), v10.EdgeRespawnZ(side), "場邊 z " + side);
            }

            Assert.IsTrue(v10.EncircleEnabled, "V0100 包夾");
            Assert.IsTrue(v10.RageEnabled, "V0100 狂怒");
            Assert.IsTrue(v10.SanctuaryEnabled, "V0100 聖所");
            Assert.IsTrue(v10.SiegeEnabled, "V0100 圍城");
            Assert.IsTrue(v10.TimeLimitEnabled, "V0100 倒數");
            Assert.IsTrue(v10.PacedScoringEnabled, "V0100 慢計分");

            Assert.IsFalse(v9.SanctuaryEnabled, "V090 聖所");
            Assert.IsFalse(v9.SiegeEnabled, "V090 圍城");
            Assert.IsFalse(v9.TimeLimitEnabled, "V090 倒數");
            Assert.IsFalse(v9.PacedScoringEnabled, "V090 慢計分");

            var v8 = CaptureBoardSpec.V080Seven;
            Assert.IsFalse(v8.SanctuaryEnabled, "V080 聖所");
            Assert.IsFalse(v8.SiegeEnabled, "V080 圍城");
            Assert.IsFalse(v8.TimeLimitEnabled, "V080 倒數");
            Assert.IsFalse(v8.PacedScoringEnabled, "V080 慢計分");
        }

        // ── V10-A11 HUD 字串表：Clock（無條件進位、0～900）、Sanctuary（常駐）、Siege（0～14） ──
        [Test]
        public void V10A11_ClockSanctuaryAndSiegeLabels_RoundUp_AndAreCached()
        {
            Assert.AreEqual("15:00", CaptureHudLabels.Clock(900f));
            Assert.AreEqual("15:00", CaptureHudLabels.Clock(899.75f));
            Assert.AreEqual("14:59", CaptureHudLabels.Clock(899f));
            Assert.AreEqual("1:00", CaptureHudLabels.Clock(60f));
            Assert.AreEqual("1:00", CaptureHudLabels.Clock(59.5f));
            Assert.AreEqual("0:01", CaptureHudLabels.Clock(0.25f));
            Assert.AreEqual("0:00", CaptureHudLabels.Clock(0f));
            Assert.AreEqual("0:00", CaptureHudLabels.Clock(-1f));
            Assert.AreEqual("SANCT 15%", CaptureHudLabels.Sanctuary());
            Assert.AreEqual("SIEGE 14%", CaptureHudLabels.Siege(14));
            Assert.AreEqual("SIEGE 0%", CaptureHudLabels.Siege(0));

            // 條文「每個兩次查表 ReferenceEquals」：上面列的每一個輸入都要驗。
            float[] clockInputs = { 900f, 899.75f, 899f, 60f, 59.5f, 0.25f, 0f, -1f };
            foreach (float seconds in clockInputs)
                Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Clock(seconds), CaptureHudLabels.Clock(seconds)), "Clock(" + seconds + ") 兩次查表不是同一個字串");
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Sanctuary(), CaptureHudLabels.Sanctuary()));
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Siege(14), CaptureHudLabels.Siege(14)));
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Siege(0), CaptureHudLabels.Siege(0)));
        }

        // ── V10-A12 HUD 版面：640×480@dpi0 精確矩形；VA24 全部裝置＋844×390@dpi0＋1688×780@dpi320 都在畫面內
        // 且不與左側面板／MatchPanel／Capture／符印鈕重疊；VA24 五條不受影響（不改既有矩形算式）。
        private const float FallbackDpi = 160f; // PlayerInputService.FallbackDpi（同 CaptureHudAndLayoutTests 的作法）

        private static readonly float[][] DeviceConfigs =
        {
            new[] { 1688f, 780f, 192f },
            new[] { 780f, 1688f, 192f },
            new[] { 2532f, 1170f, 288f },
            new[] { 844f, 390f, 0f },
            new[] { 1136f, 640f, 192f },
            new[] { 1688f, 780f, 320f }, // V10-A12 額外要求
        };

        private static DebugHudLayout Compute(float[] config)
        {
            return DebugHudLayout.Compute(config[0], config[1], config[2], true, true, true, true);
        }

        private static HudRect[] LeftPanelRects(DebugHudLayout layout)
        {
            return new[]
            {
                layout.Mode, layout.Hitbox, layout.Latency, layout.Grid, layout.EnemyWall, layout.Turret,
                layout.Water, layout.Fire, layout.Wind, layout.Elem
            };
        }

        // 符印鈕是螢幕座標（原點左下）；HUD 是 IMGUI 座標（原點左上、已乘 Scale）。換回 GUI 座標再比
        // （比照 CaptureHudAndLayoutTests.RuneButtonInGuiSpace，本檔獨立實作一份，不改既有檔）。
        private static HudRect RuneButtonInGuiSpace(float[] config, float scale)
        {
            float dpi = config[2];
            float pixelsPerMillimeter = GestureMath.MillimetersToPixels(1f, dpi, FallbackDpi);
            ScreenRegion button = RuneButtonLayout.Compute(config[0], config[1], pixelsPerMillimeter).Button;

            float guiXMin = button.XMin / scale;
            float guiXMax = button.XMax / scale;
            float guiYMin = (config[1] - button.YMax) / scale;
            float guiYMax = (config[1] - button.YMin) / scale;
            return new HudRect(guiXMin, guiYMin, guiXMax - guiXMin, guiYMax - guiYMin);
        }

        [Test]
        public void V10A12_MatchClock_ExactRectAt640x480Dpi0()
        {
            DebugHudLayout layout = Compute(new[] { 640f, 480f, 0f });
            Assert.AreEqual(276f, layout.MatchClock.X, 1e-4f);
            Assert.AreEqual(8f, layout.MatchClock.Y, 1e-4f);
            Assert.AreEqual(88f, layout.MatchClock.Width, 1e-4f);
            Assert.AreEqual(22f, layout.MatchClock.Height, 1e-4f);
            Assert.AreEqual(276f, layout.SanctuaryRow.X, 1e-4f);
            Assert.AreEqual(30f, layout.SanctuaryRow.Y, 1e-4f);
            Assert.AreEqual(88f, layout.SanctuaryRow.Width, 1e-4f);
            Assert.AreEqual(22f, layout.SanctuaryRow.Height, 1e-4f);
        }

        [Test]
        public void V10A12_MatchClockAndSanctuaryRow_OnScreen_AndNeverOverlapAnythingElse()
        {
            for (int i = 0; i < DeviceConfigs.Length; i++)
            {
                float[] config = DeviceConfigs[i];
                DebugHudLayout layout = Compute(config);
                string tag = config[0] + "x" + config[1] + "@" + config[2] + "：";

                foreach (HudRect rect in new[] { layout.MatchClock, layout.SanctuaryRow })
                {
                    float leftPx = rect.XMin * layout.Scale;
                    float rightPx = rect.XMax * layout.Scale;
                    float bottomPx = rect.YMax * layout.Scale;
                    Assert.GreaterOrEqual(leftPx, 0f, tag + "矩形左緣在畫面內");
                    Assert.LessOrEqual(rightPx, config[0], tag + "矩形右緣在畫面內");
                    Assert.LessOrEqual(bottomPx, config[1], tag + "矩形底緣在畫面內");
                }

                HudRect[] left = LeftPanelRects(layout);
                HudRect rune = RuneButtonInGuiSpace(config, layout.Scale);
                foreach (HudRect rect in new[] { layout.MatchClock, layout.SanctuaryRow })
                {
                    for (int r = 0; r < left.Length; r++)
                        Assert.IsFalse(rect.Overlaps(left[r]), tag + "蓋住左側鈕 " + r);
                    Assert.IsFalse(rect.Overlaps(layout.MatchPanel), tag + "蓋住 MatchPanel");
                    Assert.IsFalse(rect.Overlaps(layout.Capture), tag + "蓋住 Capture 鈕");
                    Assert.IsFalse(rect.Overlaps(rune), tag + "蓋住符印鈕");
                }
                Assert.IsFalse(layout.MatchClock.Overlaps(layout.SanctuaryRow), tag + "MatchClock 與 SanctuaryRow 互相重疊");
            }
        }
    }
}
