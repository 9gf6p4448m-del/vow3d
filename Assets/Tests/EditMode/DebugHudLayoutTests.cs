using NUnit.Framework;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests.EditMode
{
    // PHASE2_BATCH4_PLAN.md §5 V4-o／V4-p：四顆新鈕的版面不重疊、既有六個矩形一像素不動。
    //
    // 為什麼是 EditMode 而不是 PlayMode：`RecalculateLayout` 原本直接讀 `Screen.width/height/dpi`，
    // batchmode 改不了；把版面計算搬進 `Core/Logic/DebugHudLayout`（零 UnityEngine）之後就可以注入。
    // 這裡同時看得到 `Vow.Input.RuneButtonLayout`（符印鈕），兩個測試 asmdef 都沒有引用 `Vow.UI`
    // 而 V6-b 禁止改 asmdef——所以計算入口是 public 而不是計畫原文寫的 internal。
    public sealed class DebugHudLayoutTests
    {
        // 線上實測過的裝置像素（v0.5.0 手機模擬）＋ dpi 回 0 的 WebGL 退路
        private static readonly float[][] DeviceConfigs =
        {
            new[] { 1688f, 780f, 192f },   // 橫式（使用者實際用橫式玩）
            new[] { 780f, 1688f, 192f },   // 直式
            new[] { 2532f, 1170f, 288f }   // 高 DPR 橫式
        };

        private static readonly float[] DpiZeroConfig = { 844f, 390f, 0f };

        private const float FallbackDpi = 160f;          // PlayerInputService.FallbackDpi
        private const bool HasLatencyRow = true;         // 灰盒場景實際的組態
        private const bool HasGridRow = true;
        private const bool HasWallOrTurretRow = true;

        private static DebugHudLayout Compute(float[] config)
        {
            return DebugHudLayout.Compute(config[0], config[1], config[2],
                                          HasLatencyRow, HasGridRow, HasWallOrTurretRow);
        }

        private static HudRect[] NewButtons(DebugHudLayout layout)
        {
            return new[] { layout.Water, layout.Fire, layout.Wind, layout.Elem };
        }

        private static HudRect[] ExistingButtons(DebugHudLayout layout)
        {
            return new[] { layout.Mode, layout.Hitbox, layout.Latency, layout.Grid, layout.EnemyWall, layout.Turret };
        }

        // 符印鈕是螢幕座標（原點左下）；HUD 是 IMGUI 座標（原點左上、已乘 Scale）。換回 GUI 座標再比。
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

        private static string Describe(float[] config)
        {
            return config[0] + "x" + config[1] + " @" + config[2] + "dpi";
        }

        private static void AssertNoOverlaps(float[] config)
        {
            DebugHudLayout layout = Compute(config);
            HudRect[] added = NewButtons(layout);
            HudRect[] existing = ExistingButtons(layout);

            // ① 四個新 rect 兩兩不相交
            for (int a = 0; a < added.Length; a++)
                for (int b = a + 1; b < added.Length; b++)
                    Assert.IsFalse(added[a].Overlaps(added[b]),
                        Describe(config) + "：新鈕 " + a + " 與 " + b + " 重疊");

            // ② 四個新 rect 與既有六個不相交
            for (int a = 0; a < added.Length; a++)
                for (int e = 0; e < existing.Length; e++)
                    Assert.IsFalse(added[a].Overlaps(existing[e]),
                        Describe(config) + "：新鈕 " + a + " 蓋住了既有鈕 " + e);

            // ③ 四個新 rect 與符印鈕不相交（蓋住的話按了會立牆）
            HudRect rune = RuneButtonInGuiSpace(config, layout.Scale);
            for (int a = 0; a < added.Length; a++)
                Assert.IsFalse(added[a].Overlaps(rune),
                    Describe(config) + "：新鈕 " + a + " 蓋住了符印鈕");
        }

        [Test]
        public void V4o_TheFourNewButtons_DoNotOverlapAnythingOnAnyOfTheThreeDeviceSizes()
        {
            for (int i = 0; i < DeviceConfigs.Length; i++) AssertNoOverlaps(DeviceConfigs[i]);
        }

        [Test]
        public void V4o_TheWholePanelStaysOnScreen_OnAllThreeDeviceSizes()
        {
            for (int i = 0; i < DeviceConfigs.Length; i++)
            {
                float[] config = DeviceConfigs[i];
                DebugHudLayout layout = Compute(config);
                float bottomPixels = layout.Elem.YMax * layout.Scale;
                Assert.LessOrEqual(bottomPixels, config[1],
                    Describe(config) + "：面板底緣 " + bottomPixels + "px 超出畫面高度 " + config[1]
                    + "px（手機上按不到最後一列）");
            }
        }

        // dpi 回 0 的退路（_scale = 1）：①②③ 照驗；④ 不當及格線——v0.5.0 既有面板在這組就已經超出 390。
        // 實測值寫進回報（V4-o 的要求）。
        [Test]
        public void V4o_WithDpiZero_TheButtonsStillDoNotOverlap_EvenThoughThePanelOverflows()
        {
            AssertNoOverlaps(DpiZeroConfig);

            DebugHudLayout layout = Compute(DpiZeroConfig);
            Assert.AreEqual(1f, layout.Scale, 1e-6f, "dpi 回 0 時 Scale 必須退回 1");
            Assert.Greater(layout.Turret.YMax, 0f);
            Assert.Greater(layout.Elem.YMax, layout.Turret.YMax, "新的兩列要接在 TURRET 那一列之後");
        }

        // V4-p：既有六個矩形與 v0.5.0 的值逐值相同。
        // 期望值由**在測試裡獨立重寫一次 v0.5.0 的算式**產生（不是從 DebugHudLayout 抄回來），
        // 所以為了塞新鈕去挪既有鈕一定會紅。
        [Test]
        public void V4p_TheSixExistingRects_AreBitForBitTheSameAsInVZeroPointFive()
        {
            const float pad = 8f;
            const float row = 22f;
            const float panelWidth = 250f;
            const float infoRows = 10f;

            float[][] configs =
            {
                DeviceConfigs[0], DeviceConfigs[1], DeviceConfigs[2], DpiZeroConfig
            };

            for (int i = 0; i < configs.Length; i++)
            {
                float[] config = configs[i];
                DebugHudLayout layout = Compute(config);

                float y = pad + row * infoRows + pad;
                float buttonWidth = (panelWidth - pad * 3f) * 0.5f;
                AssertRect(new HudRect(pad * 2f, y, buttonWidth, row * 1.6f), layout.Mode, config, "_modeRect");
                AssertRect(new HudRect(pad * 3f + buttonWidth, y, buttonWidth, row * 1.6f), layout.Hitbox, config, "_hitboxRect");
                y += row * 1.6f + pad;
                AssertRect(new HudRect(pad * 2f, y, panelWidth - pad * 2f, row * 1.6f), layout.Latency, config, "_latencyRect");
                y += row * 1.6f + pad;   // HasLatencyRow
                AssertRect(new HudRect(pad * 2f, y, panelWidth - pad * 2f, row * 1.6f), layout.Grid, config, "_gridRect");
                y += row * 1.6f + pad;   // HasGridRow
                AssertRect(new HudRect(pad * 2f, y, buttonWidth, row * 1.6f), layout.EnemyWall, config, "_enemyWallRect");
                AssertRect(new HudRect(pad * 3f + buttonWidth, y, buttonWidth, row * 1.6f), layout.Turret, config, "_turretRect");

                float expectedScale = config[2] > 0f && config[2] / 160f > 1f ? config[2] / 160f : 1f;
                Assert.AreEqual(expectedScale, layout.Scale, 1e-6f, Describe(config) + "：_scale 變了");
            }
        }

        private static void AssertRect(HudRect expected, HudRect actual, float[] config, string name)
        {
            Assert.AreEqual(expected.X, actual.X, 1e-6f, Describe(config) + " " + name + ".x");
            Assert.AreEqual(expected.Y, actual.Y, 1e-6f, Describe(config) + " " + name + ".y");
            Assert.AreEqual(expected.Width, actual.Width, 1e-6f, Describe(config) + " " + name + ".width");
            Assert.AreEqual(expected.Height, actual.Height, 1e-6f, Describe(config) + " " + name + ".height");
        }
    }
}
