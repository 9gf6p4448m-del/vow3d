using NUnit.Framework;
using Vow.Core;
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

        private static readonly float[][] TalentDeviceConfigs =
        {
            new[] { 640f, 360f, 0f },
            new[] { 844f, 390f, 0f },
            new[] { 1280f, 720f, 320f },
        };

        private static ScreenRegion ScreenRect(HudRect rect, float scale, float screenHeight)
        {
            return new ScreenRegion(rect.XMin * scale, screenHeight - rect.YMax * scale,
                                    rect.XMax * scale, screenHeight - rect.YMin * scale);
        }

        [Test]
        public void C02_TalentPanelHasFrozenPositionAndDoesNotOverlapExistingControls()
        {
            for (int i = 0; i < TalentDeviceConfigs.Length; i++)
            {
                float[] config = TalentDeviceConfigs[i];
                DebugHudLayout layout = DebugHudLayout.Compute(config[0], config[1], config[2],
                                                              true, true, true, true, true);
                HudRect[] parts = { layout.TalentTitle, layout.TalentFirst, layout.TalentSecond, layout.TalentThird };
                HudRect[] left = LeftPanelRects(layout);
                HudRect rune = RuneButtonInGuiSpace(config, layout.Scale);

                Assert.AreEqual(layout.MatchPanel.X - 196f, layout.TalentPanel.X, 1e-4f);
                Assert.AreEqual(176f, layout.TalentPanel.Width, 1e-4f);
                Assert.AreEqual(60f, layout.TalentTitle.Y, 1e-4f);
                Assert.AreEqual(82f, layout.TalentTitle.YMax, 1e-4f);
                Assert.AreEqual(84f, layout.TalentFirst.Y, 1e-4f);
                Assert.AreEqual(122f, layout.TalentFirst.YMax, 1e-4f);
                Assert.AreEqual(128f, layout.TalentSecond.Y, 1e-4f);
                Assert.AreEqual(166f, layout.TalentSecond.YMax, 1e-4f);
                Assert.AreEqual(172f, layout.TalentThird.Y, 1e-4f);
                Assert.AreEqual(210f, layout.TalentThird.YMax, 1e-4f);
                for (int p = 0; p < parts.Length; p++)
                {
                    HudRect part = parts[p];
                    Assert.GreaterOrEqual(part.XMin, 0f);
                    Assert.GreaterOrEqual(part.YMin, 0f);
                    Assert.LessOrEqual(part.XMax * layout.Scale, config[0]);
                    Assert.LessOrEqual(part.YMax * layout.Scale, config[1]);
                    Assert.IsFalse(part.Overlaps(layout.MatchPanel));
                    Assert.IsFalse(part.Overlaps(layout.Capture));
                    Assert.IsFalse(part.Overlaps(layout.MatchClock));
                    Assert.IsFalse(part.Overlaps(layout.SanctuaryRow));
                    Assert.IsFalse(part.Overlaps(rune));
                    for (int l = 0; l < left.Length; l++)
                        Assert.IsFalse(part.Overlaps(left[l]), config[0] + "x" + config[1] + " part " + p + " left " + l);
                }
            }
        }

        [Test]
        public void B0_ActiveElementButtonsFitAtBottomAndOffKeepsOriginalPositions()
        {
            for (int i = 0; i < TalentDeviceConfigs.Length; i++)
            {
                float[] config = TalentDeviceConfigs[i];
                DebugHudLayout off = DebugHudLayout.Compute(config[0], config[1], config[2], true, true, true);
                DebugHudLayout active = DebugHudLayout.Compute(config[0], config[1], config[2],
                                                              true, true, true, true, true);
                HudRect[] buttons = { active.Water, active.Fire, active.Wind };
                HudRect rune = RuneButtonInGuiSpace(config, active.Scale);
                float expectedRight = active.MatchPanel.X - 20f;
                Assert.AreEqual(260f, active.Water.X, 1e-4f);
                Assert.AreEqual(expectedRight, active.Wind.XMax, 1e-4f);
                for (int b = 0; b < buttons.Length; b++)
                {
                    HudRect button = buttons[b];
                    Assert.AreEqual(config[1] / active.Scale - 56f, button.Y, 1e-4f);
                    Assert.AreEqual(40f, button.Height, 1e-4f);
                    Assert.GreaterOrEqual(button.XMin, 0f);
                    Assert.LessOrEqual(button.XMax * active.Scale, config[0]);
                    Assert.LessOrEqual(button.YMax * active.Scale, config[1]);
                    Assert.IsFalse(button.Overlaps(rune));
                    Assert.IsFalse(button.Overlaps(active.TalentPanel));
                }
                Assert.AreEqual(8f, active.Fire.X - active.Water.XMax, 1e-4f);
                Assert.AreEqual(8f, active.Wind.X - active.Fire.XMax, 1e-4f);
                Assert.AreEqual(active.Water.Width, active.Fire.Width, 1e-4f);
                Assert.AreEqual(active.Fire.Width, active.Wind.Width, 1e-4f);
                Assert.AreEqual(off.Water.X, DebugHudLayout.Compute(config[0], config[1], config[2],
                                                                   true, true, true, true, false).Water.X, 1e-4f);
            }
        }

        [Test]
        public void B0_TallLandscapeKeepsElementButtonsBelowTalentPanelAndAwayFromGroundTapBand()
        {
            DebugHudLayout layout = DebugHudLayout.Compute(640f, 480f, 96f,
                                                           true, true, true, true, true);
            Assert.AreEqual(layout.TalentPanel.YMax + 6f, layout.Water.Y, 1e-4f);
            Assert.AreEqual(layout.Water.Y, layout.Fire.Y, 1e-4f);
            Assert.AreEqual(layout.Water.Y, layout.Wind.Y, 1e-4f);
            Assert.IsFalse(layout.Water.Overlaps(layout.TalentPanel));
            Assert.IsFalse(layout.Fire.Overlaps(layout.TalentPanel));
            Assert.IsFalse(layout.Wind.Overlaps(layout.TalentPanel));
            Assert.Less(layout.Wind.YMax, 480f);
        }

        [Test]
        public void C03_TalentButtonsTakePriorityOverPanelAndHiddenPanelDoesNotStealInput()
        {
            for (int i = 0; i < TalentDeviceConfigs.Length; i++)
            {
                float[] config = TalentDeviceConfigs[i];
                float screenWidth = config[0];
                float screenHeight = config[1];
                DebugHudLayout layout = DebugHudLayout.Compute(screenWidth, screenHeight, config[2],
                                                              true, true, true, true, true);
                InputRoutingManager routing = new InputRoutingManager { EdgeMarginPixels = 0f };
                // 正式 HUD 在 ConfigureCapture 前有十區，加上 CAPTURE 一區；本盤四區是第 12～15 區。
                for (int existing = 0; existing < 11; existing++)
                    Assert.AreEqual(existing, routing.RegisterUiRegion(default));
                int first = routing.RegisterUiRegion(ScreenRect(layout.TalentFirst, layout.Scale, screenHeight));
                int second = routing.RegisterUiRegion(ScreenRect(layout.TalentSecond, layout.Scale, screenHeight));
                int third = routing.RegisterUiRegion(ScreenRect(layout.TalentThird, layout.Scale, screenHeight));
                int background = routing.RegisterUiRegion(ScreenRect(layout.TalentPanel, layout.Scale, screenHeight));
                Assert.AreEqual(14, background);
                HudRect rune = RuneButtonInGuiSpace(config, layout.Scale);
                routing.SetRuneZone(ScreenRect(rune, layout.Scale, screenHeight));

                float x = (layout.TalentFirst.XMin + layout.TalentFirst.XMax) * 0.5f * layout.Scale;
                float y = screenHeight - (layout.TalentFirst.YMin + layout.TalentFirst.YMax) * 0.5f * layout.Scale;
                Assert.AreEqual(TouchRoute.UiRegion, routing.Route(x, y, screenWidth, screenHeight,
                                                                   ControlMode.ModeA_FullScreenFlick, out int hit));
                Assert.AreEqual(first, hit);
                Assert.AreEqual(TouchRoute.UiRegion, routing.Route(x, screenHeight - 125f * layout.Scale,
                                                                   screenWidth, screenHeight, ControlMode.ModeA_FullScreenFlick, out hit));
                Assert.AreEqual(background, hit); // 兩鈕間 122～128 的空隙
                Assert.AreEqual(TouchRoute.UiRegion, routing.Route(x, screenHeight - 70f * layout.Scale,
                                                                   screenWidth, screenHeight, ControlMode.ModeA_FullScreenFlick, out hit));
                Assert.AreEqual(background, hit); // 標題
                float runeX = (rune.XMin + rune.XMax) * 0.5f * layout.Scale;
                float runeY = screenHeight - (rune.YMin + rune.YMax) * 0.5f * layout.Scale;
                Assert.AreEqual(TouchRoute.Rune, routing.Route(runeX, runeY, screenWidth, screenHeight,
                                                               ControlMode.ModeA_FullScreenFlick, out hit));
                routing.SetUiRegionActive(first, false);
                routing.SetUiRegionActive(second, false);
                routing.SetUiRegionActive(third, false);
                routing.SetUiRegionActive(background, false);
                Assert.AreEqual(TouchRoute.World, routing.Route(x, y, screenWidth, screenHeight,
                                                                ControlMode.ModeA_FullScreenFlick, out hit));
                Assert.AreEqual(-1, hit);
            }
        }
    }
}
