using System.Reflection;
using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests
{
    // 弓拖曳瞄準凍結驗收 B2（router，FakeSink）。凍結檔：vow-toolchain/acceptance-bowaim-20261003.md。
    // 刻意只「直接」引用基底 ab45498 也有的型別：替身的 OnActionButtonDragged 在基底只是多出來的公開方法（編得過、永遠收不到），
    // router 的 PixelsPerMillimeter 以反射寫入（基底沒有就略過）——讓同一份測試在基底紅在「拖曳沒送出」這個行為斷言（B11）。
    public sealed class BowAimRouterTests
    {
        private const float W = 844f, H = 390f, Ppmm = 6.3f;

        private sealed class DragSink : ITouchGestureSink, IActionButtonSink
        {
            public int Taps, Flicks, Other, AttackPressed, DashPressed, AttackReleased, AttackCanceled, OtherReleased, OtherCanceled;
            public int AttackDrags, OtherDrags;
            public float LastDx = float.NaN, LastDy = float.NaN;
            public bool DragAfterRelease;
            public void OnWorldTap(float x, float y) { Taps++; }
            public void OnCadenceFlick(float x, float y) { Flicks++; }
            public void OnUiRegionTapped(int id) { Other++; }
            public void OnRuneDragUpdated(float x, float y, float amount) { Other++; }
            public void OnRuneQuickCast() { Other++; }
            public void OnRuneReleased(float x, float y, float amount) { Other++; }
            public void OnRuneCancelled() { Other++; }
            public void OnActionButtonPressed(LabActionButton button)
            {
                if (button == LabActionButton.Attack) AttackPressed++;
                else if (button == LabActionButton.Dash) DashPressed++;
            }
            public void OnActionButtonReleased(LabActionButton button, float heldSeconds)
            {
                if (button == LabActionButton.Attack) AttackReleased++; else OtherReleased++;
            }
            public void OnActionButtonCanceled(LabActionButton button)
            {
                if (button == LabActionButton.Attack) AttackCanceled++; else OtherCanceled++;
            }
            public void OnActionButtonDragged(LabActionButton button, float dxMillimeters, float dyMillimeters)
            {
                if (button != LabActionButton.Attack) { OtherDrags++; return; }
                if (AttackReleased + AttackCanceled > 0) DragAfterRelease = true;
                AttackDrags++;
                LastDx = dxMillimeters;
                LastDy = dyMillimeters;
            }
        }

        private static TouchGestureRouter Make(out DragSink sink, out LabActionButtonLayout layout)
        {
            InputRoutingManager routing = new InputRoutingManager();
            routing.SetRuneZone(RuneButtonLayout.Compute(W, H, Ppmm).Button);
            sink = new DragSink();
            layout = LabActionButtonLayout.Compute(W, H, Ppmm);
            TouchGestureRouter r = new TouchGestureRouter(routing, sink, ControlMode.ModeA_FullScreenFlick)
            {
                ScreenWidth = W, ScreenHeight = H, MinRadiusPixels = 10f, JoystickRadiusPixels = 50f,
                MoveZone = new ScreenRegion(0f, 0f, W * 0.42f, H * 0.55f), RuneSaturationPixels = 80f, RuneTapSlopPixels = 20f,
                ActionButtons = layout, ActionButtonsEnabled = true
            };
            FieldInfo ppmm = typeof(TouchGestureRouter).GetField("PixelsPerMillimeter", BindingFlags.Instance | BindingFlags.Public);
            if (ppmm != null) ppmm.SetValue(r, Ppmm);
            r.SetThirdPersonEnabled(true);
            return r;
        }

        private static float Cx(ScreenRegion r) => (r.XMin + r.XMax) * .5f;
        private static float Cy(ScreenRegion r) => (r.YMin + r.YMax) * .5f;

        [Test] // B2：ATK 起手的 Moved→拖曳通知帶名目 mm 位移；Pressed／Released 次數同 A2；不轉鏡頭、不點擊
        public void B2_AttackDrag_SendsMillimetreDisplacement_NoCameraLook_PressReleaseCountsUnchanged()
        {
            TouchGestureRouter r = Make(out DragSink sink, out LabActionButtonLayout layout);
            float x = Cx(layout.Attack), y = Cy(layout.Attack);
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 10.0, 10.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackPressed, "按下送一次 Pressed（同 A2）");
            Assert.AreEqual(0, sink.AttackDrags, "還沒動：不送拖曳");

            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Moved, x + 63f, y - 12.6f, 10.2, 10.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackDrags, "ATK 起手的 Moved 要送拖曳通知");
            Assert.AreEqual(10f, sink.LastDx, 1e-4f, "水平位移 63px ÷ 6.3px/mm＝10mm（相對按下點）");
            Assert.AreEqual(-2f, sink.LastDy, 1e-4f, "垂直位移 −12.6px＝−2mm");
            Assert.AreEqual(0f, r.LookDeltaX, "從 ATK 起手的拖曳不得轉鏡頭（X）");
            Assert.AreEqual(0f, r.LookDeltaY, "從 ATK 起手的拖曳不得轉鏡頭（Y）");

            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Stationary, x + 63f, y - 12.6f, 10.3, 10.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackDrags, "位置沒變不重送");
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Moved, x - 94.5f, y, 10.4, 10.0); r.EndFrame();
            Assert.AreEqual(2, sink.AttackDrags);
            Assert.AreEqual(-15f, sink.LastDx, 1e-4f, "往左 94.5px＝−15mm（仍相對按下點，不是相對上一筆）");
            Assert.AreEqual(0f, sink.LastDy, 1e-4f);

            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Ended, x - 31.5f, y, 10.75, 10.0); r.EndFrame();
            Assert.AreEqual(3, sink.AttackDrags, "放開那一筆位置有變：先送最後位移");
            Assert.AreEqual(-5f, sink.LastDx, 1e-4f);
            Assert.IsFalse(sink.DragAfterRelease, "最後位移必須在 Released 之前送");
            Assert.AreEqual(1, sink.AttackReleased, "放開送一次 Released（同 A2）");
            Assert.AreEqual(0, sink.AttackCanceled);
            Assert.AreEqual(1, sink.AttackPressed, "Pressed 仍只一次");
            Assert.AreEqual(0f, r.LookDeltaX, "整段都沒轉鏡頭");
            Assert.AreEqual(0, sink.Taps + sink.Flicks + sink.Other, "不觸發世界點擊／微彈／符印");
            Assert.AreEqual(0, r.ActiveSlotCount);
        }

        [Test] // B2：右半屏拖鏡頭照舊；同時按住 ATK 拖曳，鏡頭只吃右半屏那根手指的位移（不雙重轉鏡頭）
        public void B2_RightHalfLookUnchanged_AttackDragDoesNotAddToLook()
        {
            TouchGestureRouter r = Make(out DragSink sink, out LabActionButtonLayout layout);
            float ax = Cx(layout.Attack), ay = Cy(layout.Attack);
            float lx = W * 0.6f, ly = H * 0.75f;
            Assert.IsFalse(layout.Attack.Contains(lx, ly), "看鏡頭的手指不在 ATK 上");
            r.BeginFrame();
            r.ProcessTouch(1, TouchPhaseKind.Began, ax, ay, 0.0, 0.0);
            r.ProcessTouch(2, TouchPhaseKind.Began, lx, ly, 0.0, 0.0);
            r.EndFrame();
            r.BeginFrame();
            r.ProcessTouch(1, TouchPhaseKind.Moved, ax + 50f, ay, 0.1, 0.0);
            r.ProcessTouch(2, TouchPhaseKind.Moved, lx + 30f, ly, 0.1, 0.0);
            r.EndFrame();
            Assert.AreEqual(1, sink.AttackDrags, "ATK 手指送拖曳");
            Assert.AreEqual(30f, r.LookDeltaX, 1e-4f, "鏡頭只吃右半屏手指的 30px（ATK 的 50px 不疊上去）");
            r.ConsumeLook();
            r.BeginFrame();
            r.ProcessTouch(1, TouchPhaseKind.Moved, ax + 80f, ay, 0.2, 0.0);
            r.ProcessTouch(2, TouchPhaseKind.Stationary, lx + 30f, ly, 0.2, 0.0);
            r.EndFrame();
            Assert.AreEqual(0f, r.LookDeltaX, "只動 ATK 手指：鏡頭不動");
            Assert.AreEqual(2, sink.AttackDrags);
        }

        [Test] // B2：作廢後（觸控 Canceled／整批作廢）不再送拖曳；DASH 起手拖曳不送拖曳（本批只有 ATK）
        public void B2_NoDragAfterCancel_DashDragSendsNothing()
        {
            TouchGestureRouter r = Make(out DragSink sink, out LabActionButtonLayout layout);
            float x = Cx(layout.Attack), y = Cy(layout.Attack);
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 0.0, 0.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Moved, x + 20f, y, 0.1, 0.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackDrags);
            r.CancelActiveTouches();
            Assert.AreEqual(1, sink.AttackCanceled);
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Moved, x + 60f, y, 0.2, 0.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Ended, x + 90f, y, 0.3, 0.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackDrags, "作廢後的拖曳／放開不再送拖曳");
            Assert.AreEqual(0, sink.AttackReleased);

            float dx = Cx(layout.Dash), dy = Cy(layout.Dash);
            r.BeginFrame(); r.ProcessTouch(2, TouchPhaseKind.Began, dx, dy, 1.0, 1.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(2, TouchPhaseKind.Moved, dx - 60f, dy, 1.1, 1.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(2, TouchPhaseKind.Ended, dx - 60f, dy, 1.2, 1.0); r.EndFrame();
            Assert.AreEqual(1, sink.DashPressed);
            Assert.AreEqual(0, sink.OtherDrags + sink.OtherReleased + sink.OtherCanceled, "DASH 不送拖曳／放開／作廢");
            Assert.AreEqual(0f, r.LookDeltaX, "DASH 起手的拖曳也不轉鏡頭（既有行為）");
        }
    }
}
