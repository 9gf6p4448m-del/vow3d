using System;
using NUnit.Framework;
using Vow.Core;
using Vow.Input;

namespace Vow.Tests
{
    // 地脈符印的觸控路由（GDD §參-1、§捌 Phase 2「阻斷右下角符印事件向下滲透給普攻控制器」）。
    public sealed class RuneGestureRoutingTests
    {
        private const float W = 2796f;
        private const float H = 1290f;
        private const float MinRadius = 63.4f;    // 3.5mm @ 460dpi
        private const float Saturation = 253.5f;  // 14mm @ 460dpi

        // 符印按鈕在右下角；取消區在它正上方
        private const float RuneX = 2500f;
        private const float RuneY = 250f;
        private static readonly ScreenRegion RuneZone = new ScreenRegion(2350f, 100f, 2650f, 400f);
        private static readonly ScreenRegion CancelZone = new ScreenRegion(2350f, 450f, 2650f, 600f);

        private sealed class RuneSink : ITouchGestureSink
        {
            public int WorldTaps, Flicks, UiTaps, Drags, QuickCasts, Releases, Cancels;
            public float LastDirX, LastDirY, LastDistance01;
            public float ReleaseDirX, ReleaseDirY, ReleaseDistance01;

            public void OnWorldTap(float screenX, float screenY) { WorldTaps++; }
            public void OnCadenceFlick(float screenDirX, float screenDirY) { Flicks++; }
            public void OnUiRegionTapped(int regionId) { UiTaps++; }

            public void OnRuneDragUpdated(float screenDirX, float screenDirY, float distance01)
            {
                Drags++;
                LastDirX = screenDirX; LastDirY = screenDirY; LastDistance01 = distance01;
            }

            public void OnRuneQuickCast() { QuickCasts++; }

            public void OnRuneReleased(float screenDirX, float screenDirY, float distance01)
            {
                Releases++;
                ReleaseDirX = screenDirX; ReleaseDirY = screenDirY; ReleaseDistance01 = distance01;
            }

            public void OnRuneCancelled() { Cancels++; }
        }

        private static TouchGestureRouter NewRouter(ControlMode mode, out RuneSink sink)
        {
            InputRoutingManager routing = new InputRoutingManager();
            routing.SetPipZone(new ScreenRegion(0f, 0f, 760f, 760f));
            routing.SetRuneZone(RuneZone);
            routing.SetRuneCancelZone(CancelZone);

            sink = new RuneSink();
            return new TouchGestureRouter(routing, sink, mode)
            {
                ScreenWidth = W,
                ScreenHeight = H,
                MinRadiusPixels = MinRadius,
                RuneSaturationPixels = Saturation
            };
        }

        private static void Frame(TouchGestureRouter router, double now, params object[] touches)
        {
            router.BeginFrame();
            for (int i = 0; i < touches.Length; i += 5)
                router.ProcessTouch((int)touches[i], (TouchPhaseKind)touches[i + 1],
                    (float)touches[i + 2], (float)touches[i + 3], now, (double)touches[i + 4]);
            router.EndFrame();
        }

        [Test]
        public void TapOnRune_QuickCastsExactlyOnce_AndNeverReachesTheWorld()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.03, 1, TouchPhaseKind.Moved, RuneX + 10f, RuneY + 5f, 0.0); // 手指微抖，未達門檻
            Frame(router, 0.06, 1, TouchPhaseKind.Ended, RuneX + 10f, RuneY + 5f, 0.0);

            Assert.AreEqual(1, sink.QuickCasts);
            Assert.AreEqual(0, sink.WorldTaps, "符印上的點擊不得滲透成點地／點目標");
            Assert.AreEqual(0, sink.Flicks);
            Assert.AreEqual(0, sink.Releases);
            Assert.AreEqual(0, sink.Cancels);
            Assert.AreEqual(0, sink.Drags);
        }

        [Test]
        public void LongPressWithoutDrag_StillQuickCasts()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 1.5, 1, TouchPhaseKind.Stationary, RuneX, RuneY, 0.0);
            Frame(router, 3.0, 1, TouchPhaseKind.Ended, RuneX, RuneY, 0.0);

            Assert.AreEqual(1, sink.QuickCasts);
        }

        [TestCase(ControlMode.ModeA_FullScreenFlick)]
        [TestCase(ControlMode.ModeB_DualZonePip)]
        public void DragOutOverTheWorld_ReleasesOnce_AndNeverBecomesTapOrFlick(ControlMode mode)
        {
            TouchGestureRouter router = NewRouter(mode, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.02, 1, TouchPhaseKind.Moved, RuneX - 100f, RuneY, 0.0);           // 快速越過 3.5mm：在世界層這會是一次微彈
            Frame(router, 0.10, 1, TouchPhaseKind.Moved, RuneX - 600f, RuneY + 400f, 0.0);    // 已離開符印按鈕，手指在戰場上空
            Frame(router, 0.20, 1, TouchPhaseKind.Ended, RuneX - 600f, RuneY + 400f, 0.0);

            Assert.GreaterOrEqual(sink.Drags, 1);
            Assert.AreEqual(1, sink.Releases);
            Assert.AreEqual(0, sink.WorldTaps, "符印拖曳拖到戰場上空再放手，不得變成點地");
            Assert.AreEqual(0, sink.Flicks, "符印拖曳不得變成微彈滑步");
            Assert.AreEqual(0, sink.QuickCasts);
            Assert.AreEqual(0, sink.Cancels);

            double length = Math.Sqrt(600.0 * 600.0 + 400.0 * 400.0);
            Assert.AreEqual(-600.0 / length, sink.ReleaseDirX, 1e-4, "方向是連續角度，不做八向吸附");
            Assert.AreEqual(400.0 / length, sink.ReleaseDirY, 1e-4);
            Assert.AreEqual(1f, sink.ReleaseDistance01, 1e-6);
        }

        [Test]
        public void FastDrag_WithOnlyBeganAndEndedSamples_StillReleasesWithTheFinalVector()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, RuneX, RuneY + 800f, 0.0);

            Assert.AreEqual(1, sink.Releases);
            Assert.AreEqual(0, sink.QuickCasts);
            Assert.AreEqual(0f, sink.ReleaseDirX, 1e-4);
            Assert.AreEqual(1f, sink.ReleaseDirY, 1e-4);
        }

        [Test]
        public void DragThenSlideBackToOrigin_Cancels()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.1, 1, TouchPhaseKind.Moved, RuneX - 200f, RuneY, 0.0);
            Frame(router, 0.2, 1, TouchPhaseKind.Moved, RuneX - 20f, RuneY, 0.0);
            Frame(router, 0.3, 1, TouchPhaseKind.Ended, RuneX - 20f, RuneY, 0.0);

            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases, "滑回按鈕中心放手＝安全取消，不得成牆");
            Assert.AreEqual(0, sink.QuickCasts, "拖曳過的手勢不得再被當成輕點");
            Assert.AreEqual(0, sink.WorldTaps);
        }

        [Test]
        public void DragThenReleaseInsideCancelZone_Cancels()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.1, 1, TouchPhaseKind.Moved, RuneX, RuneY + 280f, 0.0);
            Frame(router, 0.2, 1, TouchPhaseKind.Ended, RuneX, RuneY + 280f, 0.0); // y=530，落在取消區內

            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases);
        }

        [Test]
        public void TapDirectlyOnCancelZone_IsAnOrdinaryWorldTap()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, 530f, 0.0);
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, RuneX, 530f, 0.0);

            Assert.AreEqual(1, sink.WorldTaps, "取消區只在符印拖曳中有意義，平時不得吃掉戰場點擊");
            Assert.AreEqual(0, sink.Cancels);
        }

        [Test]
        public void SystemCancelDuringDrag_Cancels_AndBeforeDragStaysSilent()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.1, 1, TouchPhaseKind.Moved, RuneX - 200f, RuneY, 0.0);
            Frame(router, 0.2, 1, TouchPhaseKind.Canceled, RuneX - 200f, RuneY, 0.0);
            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases);

            // 還沒拖曳就被系統中斷：不得變成極速石牆
            Frame(router, 1.0, 2, TouchPhaseKind.Began, RuneX, RuneY, 1.0);
            Frame(router, 1.1, 2, TouchPhaseKind.Canceled, RuneX, RuneY, 1.0);
            Assert.AreEqual(0, sink.QuickCasts);
            Assert.AreEqual(1, sink.Cancels);
        }

        [Test]
        public void FingerVanishingMidDrag_Cancels()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.1, 1, TouchPhaseKind.Moved, RuneX - 200f, RuneY, 0.0);
            Frame(router, 0.2); // 失焦：這一幀手指沒出現，也沒收到 Ended

            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases);
            Assert.IsFalse(router.IsRuneHeld);

            // 槽位已回收：下一次輕點照常運作
            Frame(router, 1.00, 2, TouchPhaseKind.Began, RuneX, RuneY, 1.0);
            Frame(router, 1.05, 2, TouchPhaseKind.Ended, RuneX, RuneY, 1.0);
            Assert.AreEqual(1, sink.QuickCasts);
        }

        [Test]
        public void WhileRuneIsHeld_SecondFingerOnRuneIsIgnored_AndWorldTapsStillWork()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.05, 1, TouchPhaseKind.Moved, RuneX - 200f, RuneY, 0.0);
            int dragsBefore = sink.Drags;

            // 第二根手指也按在符印上、放開：不得產生任何符印事件
            Frame(router, 0.10, 1, TouchPhaseKind.Stationary, RuneX - 200f, RuneY, 0.0, 2, TouchPhaseKind.Began, RuneX + 50f, RuneY, 0.10);
            Frame(router, 0.15, 1, TouchPhaseKind.Stationary, RuneX - 200f, RuneY, 0.0, 2, TouchPhaseKind.Ended, RuneX + 50f, RuneY, 0.10);
            Assert.AreEqual(0, sink.QuickCasts);
            Assert.AreEqual(0, sink.Releases);
            Assert.AreEqual(0, sink.Cancels);

            // 第三根手指點戰場：符印按住期間不得鎖死其他輸入
            Frame(router, 0.20, 1, TouchPhaseKind.Stationary, RuneX - 200f, RuneY, 0.0, 3, TouchPhaseKind.Began, 1400f, 700f, 0.20);
            Frame(router, 0.25, 1, TouchPhaseKind.Stationary, RuneX - 200f, RuneY, 0.0, 3, TouchPhaseKind.Ended, 1400f, 700f, 0.20);
            Assert.AreEqual(1, sink.WorldTaps);

            Frame(router, 0.30, 1, TouchPhaseKind.Ended, RuneX - 200f, RuneY, 0.0);
            Assert.AreEqual(1, sink.Releases);
            Assert.GreaterOrEqual(sink.Drags, dragsBefore);
        }

        [Test]
        public void SwitchingModeMidDrag_Cancels_AndTheFingerIsDeadUntilLifted()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.1, 1, TouchPhaseKind.Moved, RuneX - 200f, RuneY, 0.0);
            router.ActiveMode = ControlMode.ModeB_DualZonePip;
            Assert.AreEqual(1, sink.Cancels);

            Frame(router, 0.2, 1, TouchPhaseKind.Moved, RuneX - 400f, RuneY, 0.0);
            Frame(router, 0.3, 1, TouchPhaseKind.Ended, RuneX - 400f, RuneY, 0.0);
            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases);
            Assert.AreEqual(0, sink.WorldTaps);
        }

        [Test]
        public void Distance01_IsZeroAtThreshold_OneAtSaturation_ClampedBeyond_AndMonotonic()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);
            Frame(router, 0.0, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);

            Frame(router, 0.1, 1, TouchPhaseKind.Moved, RuneX - MinRadius, RuneY, 0.0);
            Assert.AreEqual(0f, sink.LastDistance01, 1e-4);

            float previous = sink.LastDistance01;
            for (int i = 1; i <= 10; i++)
            {
                float reach = MinRadius + (Saturation - MinRadius) * i / 10f;
                Frame(router, 0.1 + i * 0.01, 1, TouchPhaseKind.Moved, RuneX - reach, RuneY, 0.0);
                Assert.Greater(sink.LastDistance01, previous);
                previous = sink.LastDistance01;
            }
            Assert.AreEqual(1f, sink.LastDistance01, 1e-4);

            Frame(router, 0.5, 1, TouchPhaseKind.Moved, RuneX - Saturation * 3f, RuneY, 0.0);
            Assert.AreEqual(1f, sink.LastDistance01, 1e-6);
        }

        [Test]
        public void RuneZoneInsideEdgeDeadzone_IsStillRejected()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);
            InputRoutingManager routing = new InputRoutingManager();
            routing.SetRuneZone(new ScreenRegion(W - 300f, 0f, W, 300f));

            Assert.AreEqual(TouchRoute.Rejected, routing.Route(W - 4f, 150f, W, H, ControlMode.ModeA_FullScreenFlick, out int _));
            Assert.AreEqual(TouchRoute.Rune, routing.Route(W - 150f, 150f, W, H, ControlMode.ModeA_FullScreenFlick, out int _));
            Assert.AreEqual(0, sink.QuickCasts);
        }
    }
}
