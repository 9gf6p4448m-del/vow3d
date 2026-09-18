using System.Collections.Generic;
using NUnit.Framework;
using Vow.Core;
using Vow.Input;

namespace Vow.Tests
{
    public sealed class TouchGestureRouterTests
    {
        private const float W = 2796f;
        private const float H = 1290f;
        private const float MinRadius = 63.4f; // 3.5mm @ 460dpi

        private sealed class RecordingSink : ITouchGestureSink
        {
            public readonly List<string> Events = new List<string>();
            public int WorldTaps;
            public int Flicks;
            public int UiTaps;

            public void OnWorldTap(float screenX, float screenY)
            {
                WorldTaps++;
                Events.Add("tap@" + (int)screenX + "," + (int)screenY);
            }

            public void OnCadenceFlick(float screenDirX, float screenDirY)
            {
                Flicks++;
                Events.Add("flick:" + (int)System.Math.Round(screenDirX) + "," + (int)System.Math.Round(screenDirY));
            }

            public void OnUiRegionTapped(int regionId)
            {
                UiTaps++;
                Events.Add("ui:" + regionId);
            }

            public void OnRuneDragUpdated(float screenDirX, float screenDirY, float distance01) { Events.Add("rune-drag"); }
            public void OnRuneQuickCast() { Events.Add("rune-quick"); }
            public void OnRuneReleased(float screenDirX, float screenDirY, float distance01) { Events.Add("rune-release"); }
            public void OnRuneCancelled() { Events.Add("rune-cancel"); }
        }

        private static TouchGestureRouter NewRouter(ControlMode mode, out RecordingSink sink, out int buttonId)
        {
            InputRoutingManager routing = new InputRoutingManager();
            buttonId = routing.RegisterUiRegion(new ScreenRegion(40f, 1100f, 300f, 1200f));
            routing.SetPipZone(new ScreenRegion(0f, 0f, 760f, 760f));

            sink = new RecordingSink();
            return new TouchGestureRouter(routing, sink, mode)
            {
                ScreenWidth = W,
                ScreenHeight = H,
                MinRadiusPixels = MinRadius
            };
        }

        // 一幀內餵入多根手指：(id, phase, x, y, startTime)
        private static void Frame(TouchGestureRouter router, double now, params object[] touches)
        {
            router.BeginFrame();
            for (int i = 0; i < touches.Length; i += 5)
                router.ProcessTouch((int)touches[i], (TouchPhaseKind)touches[i + 1],
                    (float)touches[i + 2], (float)touches[i + 3], now, (double)touches[i + 4]);
            router.EndFrame();
        }

        [Test]
        public void ModeA_TapIsEmittedOnRelease_NotOnPress()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, 1500f, 600f, 0.0);
            Assert.AreEqual(0, sink.WorldTaps, "模式 A 必須等離手才能排除微彈");
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, 1504f, 602f, 0.0);

            Assert.AreEqual(1, sink.WorldTaps);
            Assert.AreEqual(0, sink.Flicks);
            Assert.AreEqual(0, router.ActiveSlotCount);
        }

        [Test]
        public void ModeA_FlickFiresOnceAtThreshold_AndSuppressesTheTap()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, 1500f, 600f, 0.0);
            Frame(router, 0.02, 1, TouchPhaseKind.Moved, 1500f, 680f, 0.0);
            Assert.AreEqual(1, sink.Flicks, "越過 3.5mm 的當下即判定");
            Assert.AreEqual("flick:0,1", sink.Events[0]);

            Frame(router, 0.04, 1, TouchPhaseKind.Moved, 1500f, 900f, 0.0);
            Frame(router, 0.06, 1, TouchPhaseKind.Ended, 1500f, 900f, 0.0);
            Assert.AreEqual(1, sink.Flicks, "同一次觸控只算一次微彈");
            Assert.AreEqual(0, sink.WorldTaps, "微彈之後離手不得補發點擊");
        }

        [Test]
        public void ModeB_WorldTapFiresOnPress_AndNothingMoreOnRelease()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeB_DualZonePip, out RecordingSink sink, out int _);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, 1800f, 600f, 0.0);
            Assert.AreEqual(1, sink.WorldTaps, "模式 B 右手點擊按下即送出");

            Frame(router, 0.03, 1, TouchPhaseKind.Moved, 1800f, 900f, 0.0);
            Frame(router, 0.06, 1, TouchPhaseKind.Ended, 1800f, 900f, 0.0);
            Assert.AreEqual(1, sink.WorldTaps);
            Assert.AreEqual(0, sink.Flicks, "模式 B 的右手不產生微彈");
        }

        [Test] // 高壓紅線：微輪盤絕不可產生移動指令
        public void ModeB_PipNeverProducesAWorldTap_NoMatterWhatTheThumbDoes()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeB_DualZonePip, out RecordingSink sink, out int _);

            Frame(router, 0.00, 7, TouchPhaseKind.Began, 300f, 300f, 0.0);
            Assert.IsTrue(router.IsPipHeld);
            Frame(router, 0.02, 7, TouchPhaseKind.Moved, 320f, 310f, 0.0);
            Assert.IsFalse(router.HasPipVector, "3.5mm 內為中立區");
            Frame(router, 0.04, 7, TouchPhaseKind.Moved, 300f, 420f, 0.0);
            Assert.IsTrue(router.HasPipVector);
            Assert.AreEqual(1f, router.PipDirY, 1e-5f);

            // 拖出判定區、拖到螢幕另一頭、再放開：仍然只是微輪盤
            Frame(router, 0.06, 7, TouchPhaseKind.Moved, 2000f, 900f, 0.0);
            Frame(router, 0.08, 7, TouchPhaseKind.Ended, 2000f, 900f, 0.0);

            Assert.AreEqual(0, sink.WorldTaps);
            Assert.IsFalse(router.IsPipHeld);
            Assert.AreEqual(0, router.ActiveSlotCount);
        }

        [Test] // 審查 r1 M-1
        public void SwitchingMode_WhileAFingerIsStillDown_DoesNotConjureAWorldTap()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            // 一根手指按在世界區沒放；另一根點 HUD 把模式切到 B
            Frame(router, 0.00, 1, TouchPhaseKind.Began, 1500f, 600f, 0.0);
            router.ActiveMode = ControlMode.ModeB_DualZonePip;

            Frame(router, 0.02, 1, TouchPhaseKind.Moved, 1502f, 601f, 0.0);
            Frame(router, 0.04, 1, TouchPhaseKind.Stationary, 1502f, 601f, 0.0);
            Assert.AreEqual(0, sink.WorldTaps, "仍按著的手指不得在新模式下被當成一次新的按下");

            Frame(router, 0.06, 1, TouchPhaseKind.Ended, 1502f, 601f, 0.0);
            Assert.AreEqual(0, sink.WorldTaps, "作廢的手指離手時也不得補發點擊");
            Assert.AreEqual(0, router.ActiveSlotCount);

            // 作廢只針對當時在場的手指；之後新的手指照常運作
            Frame(router, 0.10, 2, TouchPhaseKind.Began, 1700f, 500f, 0.10);
            Assert.AreEqual(1, sink.WorldTaps);
        }

        [Test]
        public void SwitchingMode_WhilePipIsHeld_ReleasesThePipCleanly()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeB_DualZonePip, out RecordingSink sink, out int _);

            Frame(router, 0.00, 7, TouchPhaseKind.Began, 300f, 300f, 0.0);
            Frame(router, 0.02, 7, TouchPhaseKind.Moved, 300f, 420f, 0.0);
            Assert.IsTrue(router.HasPipVector);

            router.ActiveMode = ControlMode.ModeA_FullScreenFlick;
            Assert.IsFalse(router.IsPipHeld);

            Frame(router, 0.04, 7, TouchPhaseKind.Moved, 300f, 600f, 0.0);
            Frame(router, 0.06, 7, TouchPhaseKind.Ended, 300f, 600f, 0.0);
            Assert.AreEqual(0, sink.WorldTaps);
            Assert.AreEqual(0, sink.Flicks, "原本的微輪盤手指不得在模式 A 下變成一次微彈");
            Assert.IsFalse(router.IsPipHeld);
        }

        [Test] // 審查 r1 M-2
        public void TwoFingersEndingInTheSameFrame_NeitherIsReplayedAsANewTap()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            Frame(router, 0.00,
                1, TouchPhaseKind.Began, 1500f, 600f, 0.0,
                2, TouchPhaseKind.Began, 1900f, 700f, 0.0);
            Frame(router, 0.05,
                1, TouchPhaseKind.Ended, 1500f, 600f, 0.0,
                2, TouchPhaseKind.Ended, 1900f, 700f, 0.0);
            Assert.AreEqual(2, sink.WorldTaps);

            // 兩根都在列表中多留一幀（相位仍為 Ended）
            Frame(router, 0.06,
                1, TouchPhaseKind.Ended, 1500f, 600f, 0.0,
                2, TouchPhaseKind.Ended, 1900f, 700f, 0.0);
            Assert.AreEqual(2, sink.WorldTaps, "已結束的觸控多留一幀，兩根都不得被補一次點擊");
        }

        [Test]
        public void SameTouchIdReusedByANewTouch_IsNotMistakenForTheLingeringOne()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, 1500f, 600f, 0.0);
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, 1500f, 600f, 0.0);

            // 同一個 touchId、不同的 startTime：同一幀內按下又放開的全新點擊
            Frame(router, 0.30, 1, TouchPhaseKind.Ended, 1600f, 650f, 0.29);
            Assert.AreEqual(2, sink.WorldTaps);
        }

        [Test]
        public void FingerVanishingWithoutEnded_FreesItsSlot_SoSlotsNeverLeak()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            for (int round = 0; round < 30; round++)
            {
                Frame(router, round, round + 100, TouchPhaseKind.Began, 1500f, 600f, (double)round);
                Assert.AreEqual(1, router.ActiveSlotCount);
                Frame(router, round + 0.5); // 手指憑空消失（失焦），沒有 Ended
                Assert.AreEqual(0, router.ActiveSlotCount);
            }

            Frame(router, 99.0, 1, TouchPhaseKind.Began, 1500f, 600f, 99.0);
            Frame(router, 99.1, 1, TouchPhaseKind.Ended, 1500f, 600f, 99.0);
            Assert.AreEqual(1, sink.WorldTaps, "30 輪之後槽位仍可用");
        }

        [Test]
        public void UiTap_RequiresPressAndReleaseInsideTheSameRegion_AndNeverReachesTheWorld()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int buttonId);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, 100f, 1150f, 0.0);
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, 110f, 1155f, 0.0);
            Assert.AreEqual(1, sink.UiTaps);
            Assert.AreEqual("ui:" + buttonId, sink.Events[0]);

            Frame(router, 0.20, 2, TouchPhaseKind.Began, 100f, 1150f, 0.20);
            Frame(router, 0.25, 2, TouchPhaseKind.Ended, 1500f, 600f, 0.20); // 滑出按鈕才放開
            Assert.AreEqual(1, sink.UiTaps);
            Assert.AreEqual(0, sink.WorldTaps, "起點在 UI 上的手指不得滲透成點地");
        }

        [Test]
        public void EdgeDeadzoneTouch_ProducesNothing()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RecordingSink sink, out int _);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, 3f, 600f, 0.0);
            Frame(router, 0.02, 1, TouchPhaseKind.Moved, 200f, 600f, 0.0);
            Frame(router, 0.04, 1, TouchPhaseKind.Ended, 200f, 600f, 0.0);

            Assert.AreEqual(0, sink.Events.Count);
        }
    }
}
