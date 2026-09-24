using NUnit.Framework;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests.EditMode
{
    // V080_CAPTURE_PLAN.md §3-A：V-A23（CaptureHudLabels 字串表）、V-A24（DebugHudLayout 新增 MatchPanel/Capture）。凍結。
    public sealed class CaptureHudAndLayoutTests
    {
        [Test]
        public void VA23_ScoreTable_CoversZeroToTwelveOverOneThousand()
        {
            Assert.AreEqual("0", CaptureHudLabels.Score(0));
            Assert.AreEqual("2", CaptureHudLabels.Score(2));
            Assert.AreEqual("998", CaptureHudLabels.Score(998));
            Assert.AreEqual("1000", CaptureHudLabels.Score(1000));
            Assert.AreEqual("1012", CaptureHudLabels.Score(1012));
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Score(998), CaptureHudLabels.Score(998)));
        }

        [Test]
        public void VA23_RespawnTable_RoundsUpToTheWholeSecond()
        {
            Assert.AreEqual("RESPAWN 5", CaptureHudLabels.Respawn(5.0f));
            Assert.AreEqual("RESPAWN 5", CaptureHudLabels.Respawn(4.0625f));
            Assert.AreEqual("RESPAWN 4", CaptureHudLabels.Respawn(4.0f));
            Assert.AreEqual("RESPAWN 1", CaptureHudLabels.Respawn(0.0625f));
            Assert.IsTrue(ReferenceEquals(CaptureHudLabels.Respawn(4.0625f), CaptureHudLabels.Respawn(4.0625f)));
        }

        // 五組裝置像素（V-A24 指定）＋既有三組（DebugHudLayoutTests）延伸出的第五組 1136×640@192。
        private static readonly float[][] DeviceConfigs =
        {
            new[] { 1688f, 780f, 192f },
            new[] { 780f, 1688f, 192f },
            new[] { 2532f, 1170f, 288f },
            new[] { 844f, 390f, 0f },
            new[] { 1136f, 640f, 192f },
        };

        private const float FallbackDpi = 160f; // PlayerInputService.FallbackDpi

        private static DebugHudLayout Compute(float[] config, bool captureModeActive)
        {
            return DebugHudLayout.Compute(config[0], config[1], config[2],
                                          true, true, true, captureModeActive);
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
        // （比照 DebugHudLayoutTests.cs 的 RuneButtonInGuiSpace，本檔獨立實作一份，不改既有檔）。
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
        public void VA24_CaptureButton_DoesNotOverlapAnythingOnAnyDeviceSize()
        {
            for (int i = 0; i < DeviceConfigs.Length; i++)
            {
                float[] config = DeviceConfigs[i];
                DebugHudLayout layout = Compute(config, true);
                HudRect[] left = LeftPanelRects(layout);
                for (int r = 0; r < left.Length; r++)
                    Assert.IsFalse(layout.Capture.Overlaps(left[r]), config[0] + "x" + config[1] + "：Capture 蓋住了左側鈕 " + r);

                Assert.IsFalse(layout.Capture.Overlaps(layout.MatchPanel), config[0] + "x" + config[1] + "：Capture 蓋住了 MatchPanel");

                HudRect rune = RuneButtonInGuiSpace(config, layout.Scale);
                Assert.IsFalse(layout.Capture.Overlaps(rune), config[0] + "x" + config[1] + "：Capture 蓋住了符印鈕");
            }
        }

        [Test]
        public void VA24_CaptureButton_StaysOnScreen_OnAllFiveDeviceSizes()
        {
            for (int i = 0; i < DeviceConfigs.Length; i++)
            {
                float[] config = DeviceConfigs[i];
                DebugHudLayout layout = Compute(config, true);
                float bottomPixels = layout.Capture.YMax * layout.Scale;
                Assert.LessOrEqual(bottomPixels, config[1], config[0] + "x" + config[1] + "：Capture 底緣超出畫面");
            }
        }

        [Test]
        public void VA24_CaptureButton_ExactRectOn844x390AtDpiZero()
        {
            DebugHudLayout layout = Compute(new[] { 844f, 390f, 0f }, true);
            Assert.AreEqual(660f, layout.Capture.X, 1e-4f);
            Assert.AreEqual(132f, layout.Capture.Y, 1e-4f);
            Assert.AreEqual(176f, layout.Capture.Width, 1e-4f);
            Assert.AreEqual(35.2f, layout.Capture.Height, 1e-4f);
        }

        [Test]
        public void VA24_LeftPanel_IsUnaffectedByTheMatchPanel()
        {
            DebugHudLayout layout = Compute(new[] { 1688f, 780f, 192f }, true);
            Assert.AreEqual(495.2f, layout.PanelHeight, 1e-4f);

            for (int i = 0; i < DeviceConfigs.Length; i++)
            {
                float[] config = DeviceConfigs[i];
                if (config[2] <= 0f) continue; // dpi 回 0 那組本來就會超出畫面，不是新規範
                DebugHudLayout l = Compute(config, true);
                float bottom = (8f + l.PanelHeight) * l.Scale;
                Assert.LessOrEqual(bottom, config[1], config[0] + "x" + config[1] + "：左側面板超出畫面");
            }
        }

        [Test]
        public void VA24_MatchPanelHeight_SeventyTwoWhenOff_OneSixteenWhenCaptureModeActive()
        {
            DebugHudLayout off = Compute(new[] { 844f, 390f, 0f }, false);
            Assert.AreEqual(72f, off.MatchPanel.Height, 1e-4f);
            DebugHudLayout active = Compute(new[] { 844f, 390f, 0f }, true);
            Assert.AreEqual(116f, active.MatchPanel.Height, 1e-4f);
        }
    }
}
