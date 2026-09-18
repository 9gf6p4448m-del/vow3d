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
        private const float TapSlop = 135.8f;     // 7.5mm @ 460dpi
        private const float PxPerMm = 460f / 25.4f;

        // 幾何一律取自正式的版面計算，不手寫：手寫的取消區曾經緊貼按鈕，把「往上拉到一半就被判成取消」寫成了期望行為。
        private static readonly RuneButtonLayout Layout = RuneButtonLayout.Compute(W, H, PxPerMm, Saturation);
        private static readonly ScreenRegion RuneZone = Layout.Button;
        private static readonly ScreenRegion CancelZone = Layout.CancelZone;
        private static readonly float RuneX = (RuneZone.XMin + RuneZone.XMax) * 0.5f;
        private static readonly float RuneY = (RuneZone.YMin + RuneZone.YMax) * 0.5f;
        private static readonly float CancelY = (CancelZone.YMin + CancelZone.YMax) * 0.5f;

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
                RuneSaturationPixels = Saturation,
                RuneTapSlopPixels = TapSlop
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
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, RuneX - 800f, RuneY, 0.0);

            Assert.AreEqual(1, sink.Releases);
            Assert.AreEqual(0, sink.QuickCasts);
            Assert.AreEqual(-1f, sink.ReleaseDirX, 1e-4);
            Assert.AreEqual(0f, sink.ReleaseDirY, 1e-4);
        }

        // 覆審 r2-N4：取消區若只是一條帶子，想「用力往上甩掉這次施法」的手指會穿過它、停在上方，結果立出一面最遠的牆。
        [Test]
        public void FlingingFarAboveTheButton_StillCancels()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.20, 1, TouchPhaseKind.Moved, RuneX, RuneY + 500f, 0.0);
            Frame(router, 0.40, 1, TouchPhaseKind.Ended, RuneX, H - 5f, 0.0); // 一路甩到螢幕頂

            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases);
        }

        // 覆審 r2-N1：高手的快速方向施放——0.15 秒內往左甩 5.5mm 就放手。放手時手指仍在門檻外，方向必須保留，
        // 不得被「短促輕點」的赦免吞成英雄正前方的極速石牆。
        [Test]
        public void QuickShortAimedDrag_KeepsItsDirection()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.08, 1, TouchPhaseKind.Moved, RuneX - 70f, RuneY, 0.0);
            Frame(router, 0.15, 1, TouchPhaseKind.Ended, RuneX - 100f, RuneY, 0.0);

            Assert.AreEqual(1, sink.Releases, "快速短距離的方向施放必須照拖曳方向成牆");
            Assert.AreEqual(0, sink.QuickCasts, "方向不得被丟掉");
            Assert.AreEqual(-1f, sink.ReleaseDirX, 1e-4);
            Assert.AreEqual(0f, sink.ReleaseDirY, 1e-4);
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
            Frame(router, 0.3, 1, TouchPhaseKind.Moved, RuneX, CancelY, 0.0);
            Frame(router, 0.4, 1, TouchPhaseKind.Ended, RuneX, CancelY, 0.0);

            Assert.AreEqual(1, sink.Cancels);
            Assert.AreEqual(0, sink.Releases);
        }

        [Test]
        public void TapDirectlyOnCancelZone_IsAnOrdinaryWorldTap()
        {
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, CancelY, 0.0);
            Frame(router, 0.05, 1, TouchPhaseKind.Ended, RuneX, CancelY, 0.0);

            Assert.AreEqual(1, sink.WorldTaps, "取消區只在符印拖曳中有意義，平時不得吃掉戰場點擊");
            Assert.AreEqual(0, sink.Cancels);
        }

        // 審查 r1-C1：拖曳原點是手指按下的位置。不論按在按鈕的哪個角落，往任何方向拉滿都必須還放得出牆——
        // 尤其是螢幕上方，那是英雄前方、最需要封路的方向。
        [Test]
        public void FullStretchInAnyDirection_FromAnywhereOnTheButton_StillReleases()
        {
            float[] originsX = { RuneZone.XMin + 1f, RuneX, RuneZone.XMax - 1f };
            float[] originsY = { RuneZone.YMin + 1f, RuneY, RuneZone.YMax - 1f };

            float[] stretches = { 1f, 1.5f }; // 剛好拉滿；拉過頭五成（真人拉滿時幾乎都會拉過頭）
            int clamped = 0, total = 0;

            foreach (float stretch in stretches)
            foreach (float ox in originsX)
            foreach (float oy in originsY)
            for (int step = 0; step < 16; step++)
            {
                double angle = step * System.Math.PI / 8.0;
                float tx = ox + (float)System.Math.Cos(angle) * Saturation * stretch;
                float ty = oy + (float)System.Math.Sin(angle) * Saturation * stretch;

                // 右下角的按鈕往右、往下拖不了多遠：手指被螢幕邊緣擋住。這是版面的已知限制（螢幕右方／下方立不了遠牆），
                // 但被擋住的手勢仍然必須成牆，不得變成取消或輕點。
                bool hitEdge = tx < 0f || tx > W || ty < 0f || ty > H;
                tx = System.Math.Min(System.Math.Max(tx, 0f), W);
                ty = System.Math.Min(System.Math.Max(ty, 0f), H);
                total++;
                if (hitEdge) clamped++;

                TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);
                Frame(router, 0.0, 1, TouchPhaseKind.Began, ox, oy, 0.0);
                Frame(router, 0.3, 1, TouchPhaseKind.Moved, tx, ty, 0.0);
                Frame(router, 0.5, 1, TouchPhaseKind.Ended, tx, ty, 0.0);

                string where = "原點 (" + ox + "," + oy + ")、角度 " + (step * 22.5) + "°、拉伸 " + stretch;
                Assert.AreEqual(1, sink.Releases, "鬆手必須成牆：" + where);
                Assert.AreEqual(0, sink.Cancels, "不得落進取消區：" + where);
                Assert.AreEqual(0, sink.QuickCasts, where);
                if (!hitEdge) Assert.AreEqual(1f, sink.ReleaseDistance01, 1e-3, where);
            }

            Assert.Greater(total - clamped, total / 2, "沒被螢幕邊緣擋住的組合必須過半，否則這支測試量到的是螢幕邊緣而不是取消區");
        }

        // 審查 r1-H3：短促輕點時拇指在螢幕上滾動，位移越過 3.5mm 再回來（或沒回來）都很常見。
        [Test]
        public void QuickTapWithThumbRoll_IsStillAQuickCast()
        {
            // (滾出多遠, 放開時離原點多遠)
            float[,] rolls =
            {
                { 80f, 20f },   // 滾出 4.4mm、放開時回到 1.1mm：舊版判成「取消」＝按了沒反應
                { 130f, 0f },   // 接近 7.5mm 的上限
                { 100f, 60f }   // 放開時剛好回到 3.5mm 門檻內
            };

            for (int i = 0; i < rolls.GetLength(0); i++)
            {
                TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink sink);

                Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
                Frame(router, 0.05, 1, TouchPhaseKind.Moved, RuneX - rolls[i, 0], RuneY, 0.0);
                Frame(router, 0.12, 1, TouchPhaseKind.Ended, RuneX - rolls[i, 1], RuneY, 0.0);

                Assert.AreEqual(1, sink.QuickCasts, "短促輕點不得因為拇指滾動而被吃掉（案例 " + i + "）");
                Assert.AreEqual(0, sink.Cancels, "案例 " + i);
                Assert.AreEqual(0, sink.Releases, "案例 " + i);
                Assert.AreEqual(0, sink.WorldTaps, "案例 " + i);
            }
        }

        [Test]
        public void ThumbRollForgiveness_DoesNotSwallowDeliberateGestures()
        {
            // 慢：拖出 4.4mm、停 0.3 秒看虛影、滑回原點放手＝取消（不是輕點）
            TouchGestureRouter router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink slow);
            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.10, 1, TouchPhaseKind.Moved, RuneX - 80f, RuneY, 0.0);
            Frame(router, 0.40, 1, TouchPhaseKind.Ended, RuneX - 20f, RuneY, 0.0);
            Assert.AreEqual(1, slow.Cancels);
            Assert.AreEqual(0, slow.QuickCasts);

            // 快、滑回原點、但甩得遠：0.18 秒內拖出 11mm 又收回來＝反悔取消（拇指滾動滾不到 11mm，不是輕點）
            router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink regret);
            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.08, 1, TouchPhaseKind.Moved, RuneX - 200f, RuneY, 0.0);
            Frame(router, 0.18, 1, TouchPhaseKind.Ended, RuneX - 20f, RuneY, 0.0);
            Assert.AreEqual(1, regret.Cancels);
            Assert.AreEqual(0, regret.QuickCasts);

            // 快但遠：0.1 秒內甩出 8.8mm＝拖曳施法（不是輕點）
            router = NewRouter(ControlMode.ModeA_FullScreenFlick, out RuneSink far);
            Frame(router, 0.00, 1, TouchPhaseKind.Began, RuneX, RuneY, 0.0);
            Frame(router, 0.10, 1, TouchPhaseKind.Ended, RuneX - 160f, RuneY, 0.0);
            Assert.AreEqual(1, far.Releases);
            Assert.AreEqual(0, far.QuickCasts);
        }

        // 覆審 r2-N3／N8：期望值是手算後寫死的數字，不從 RuneButtonLayout 導出——治具其餘部分都取自受測物，
        // 這裡是唯一不會跟著幾何錯誤一起漂移的錨點。460dpi：1mm = 18.1102px。
        [Test]
        public void Layout_OnAFlagshipPhone_MatchesTheHandComputedGeometry()
        {
            RuneButtonLayout layout = RuneButtonLayout.Compute(2796f, 1290f, 460f / 25.4f, 253.5f);

            Assert.AreEqual(2433.8f, layout.Button.XMin, 0.5f, "按鈕左緣＝2796 − 4mm − 16mm");
            Assert.AreEqual(2723.6f, layout.Button.XMax, 0.5f, "按鈕右緣＝2796 − 4mm");
            Assert.AreEqual(72.4f, layout.Button.YMin, 0.5f, "按鈕下緣＝4mm");
            Assert.AreEqual(362.2f, layout.Button.YMax, 0.5f, "按鈕上緣＝4mm + 16mm");

            Assert.AreEqual(2433.8f, layout.CancelZone.XMin, 0.5f);
            Assert.AreEqual(2723.6f, layout.CancelZone.XMax, 0.5f);
            Assert.AreEqual(869.2f, layout.CancelZone.YMin, 0.5f, "取消區下緣＝按鈕上緣 + 14mm 飽和行程 + 14mm 超拉餘裕");
            Assert.AreEqual(1290f, layout.CancelZone.YMax, 0.01f, "取消區一路延伸到螢幕上緣");
        }

        // 覆審 r2-N6：矮畫布（桌機瀏覽器拉矮視窗、嵌入頁）上取消區不得被擠到消失。160dpi：1mm = 6.2992px，8mm = 50.4px。
        [Test]
        public void Layout_OnAShortCanvas_KeepsAUsableCancelZone()
        {
            const float ppm = 160f / 25.4f;
            RuneButtonLayout layout = RuneButtonLayout.Compute(1280f, 330f, ppm, 14f * ppm);

            Assert.AreEqual(126.0f, layout.Button.YMax, 0.5f);
            Assert.AreEqual(279.6f, layout.CancelZone.YMin, 0.5f, "取消區下緣被壓低到「螢幕上緣 − 8mm」");
            Assert.AreEqual(330f, layout.CancelZone.YMax, 0.01f);
            Assert.GreaterOrEqual(layout.CancelZone.YMin, layout.Button.YMax, "取消區不得壓到按鈕上");

            // 極端：連按鈕加 8mm 都放不下時，取消區貼著按鈕上緣，不得出現上下顛倒的區域
            RuneButtonLayout tiny = RuneButtonLayout.Compute(1280f, 150f, ppm, 14f * ppm);
            Assert.AreEqual(tiny.Button.YMax, tiny.CancelZone.YMin, 0.01f);
            Assert.GreaterOrEqual(tiny.CancelZone.YMax, tiny.CancelZone.YMin);
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
