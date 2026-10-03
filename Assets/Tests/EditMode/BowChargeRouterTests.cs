using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.Tests
{
    // 弓蓄力凍結驗收 A2（router，FakeSink）。凍結檔：vow-toolchain/acceptance-bowcharge-20261003.md。
    // 刻意只用基底 05f97b9 也有的型別：替身的放開／作廢方法在基底上只是多出來的公開方法（編得過、永遠收不到），
    // 讓同一份測試能在基底跑出「紅在行為斷言」（A10）。
    public sealed class BowChargeRouterTests
    {
        private const float W = 844f, H = 390f, Ppmm = 6.3f;

        private sealed class HoldSink : ITouchGestureSink, IActionButtonSink
        {
            public int Taps, Flicks, Other, AttackPressed, DashPressed, WeaponPressed;
            public int AttackReleased, OtherReleased, AttackCanceled, OtherCanceled;
            public float LastHeldSeconds = -1f;
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
                else if (button == LabActionButton.Weapon) WeaponPressed++;
            }
            public void OnActionButtonReleased(LabActionButton button, float heldSeconds)
            {
                if (button == LabActionButton.Attack) { AttackReleased++; LastHeldSeconds = heldSeconds; }
                else OtherReleased++;
            }
            public void OnActionButtonCanceled(LabActionButton button)
            {
                if (button == LabActionButton.Attack) AttackCanceled++;
                else OtherCanceled++;
            }
            public void OnActionButtonDragged(LabActionButton button, float dxMillimeters, float dyMillimeters) { }
        }

        private static TouchGestureRouter Make(out HoldSink sink, out LabActionButtonLayout layout)
        {
            InputRoutingManager routing = new InputRoutingManager();
            routing.SetRuneZone(RuneButtonLayout.Compute(W, H, Ppmm).Button);
            sink = new HoldSink();
            layout = LabActionButtonLayout.Compute(W, H, Ppmm);
            TouchGestureRouter r = new TouchGestureRouter(routing, sink, ControlMode.ModeA_FullScreenFlick)
            {
                ScreenWidth = W, ScreenHeight = H, MinRadiusPixels = 10f, JoystickRadiusPixels = 50f,
                MoveZone = new ScreenRegion(0f, 0f, W * 0.42f, H * 0.55f), RuneSaturationPixels = 80f, RuneTapSlopPixels = 20f,
                ActionButtons = layout, ActionButtonsEnabled = true
            };
            r.SetThirdPersonEnabled(true);
            return r;
        }

        private static float Cx(ScreenRegion r) => (r.XMin + r.XMax) * .5f;
        private static float Cy(ScreenRegion r) => (r.YMin + r.YMax) * .5f;

        [Test] // A2：ATK 按住 0.75s 放開→Pressed 1、Released 1（帶 0.75s）、Canceled 0
        public void A2_AttackHeldThenEnded_PressedOnce_ReleasedOnceWithHeldSeconds()
        {
            TouchGestureRouter r = Make(out HoldSink sink, out LabActionButtonLayout layout);
            float x = Cx(layout.Attack), y = Cy(layout.Attack);
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 10.0, 10.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackPressed, "按下當下送一次 Pressed");
            Assert.AreEqual(0, sink.AttackReleased, "還按著：不送 Released");
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Stationary, x, y, 10.3, 10.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Moved, x + 3f, y, 10.5, 10.0); r.EndFrame();
            Assert.AreEqual(0, sink.AttackReleased, "按住移動：不送 Released");
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Ended, x + 3f, y, 10.75, 10.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackReleased, "放開送一次 Released");
            Assert.AreEqual(0.75f, sink.LastHeldSeconds, 1e-4f, "Released 帶按住秒數");
            Assert.AreEqual(0, sink.AttackCanceled, "放開不是作廢");
            Assert.AreEqual(1, sink.AttackPressed, "Pressed 仍只一次");
            Assert.AreEqual(0, sink.Taps + sink.Flicks, "不觸發世界點擊／微彈");
            Assert.AreEqual(0, r.ActiveSlotCount);
        }

        [Test] // A2：同一幀按下又放開（快速點擊）→Released 1、秒數 0
        public void A2_AttackTapSameFrame_ReleasedOnceWithZeroSeconds()
        {
            TouchGestureRouter r = Make(out HoldSink sink, out LabActionButtonLayout layout);
            float x = Cx(layout.Attack), y = Cy(layout.Attack);
            r.ProcessTouch(7, TouchPhaseKind.Began, x, y, 3.0, 3.0);
            r.ProcessTouch(7, TouchPhaseKind.Ended, x, y, 3.0, 3.0);
            Assert.AreEqual(1, sink.AttackPressed);
            Assert.AreEqual(1, sink.AttackReleased, "快速點擊也要送 Released");
            Assert.AreEqual(0f, sink.LastHeldSeconds, 1e-6f);
            Assert.AreEqual(0, sink.AttackCanceled);
        }

        [Test] // A2：觸控 Canceled→Canceled 1、無 Released；手指從列表消失（EndFrame 回收）同理
        public void A2_AttackTouchCanceled_CanceledOnce_NoReleased()
        {
            TouchGestureRouter r = Make(out HoldSink sink, out LabActionButtonLayout layout);
            float x = Cx(layout.Attack), y = Cy(layout.Attack);
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 0.0, 0.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Canceled, x, y, 0.5, 0.0); r.EndFrame();
            Assert.AreEqual(1, sink.AttackCanceled, "觸控 Canceled→送一次 Canceled");
            Assert.AreEqual(0, sink.AttackReleased, "Canceled 不得當成 Released");

            r.BeginFrame(); r.ProcessTouch(2, TouchPhaseKind.Began, x, y, 1.0, 1.0); r.EndFrame();
            r.BeginFrame(); r.EndFrame();   // 手指沒回報就消失
            Assert.AreEqual(2, sink.AttackCanceled, "消失的手指＝作廢");
            Assert.AreEqual(0, sink.AttackReleased);
            Assert.AreEqual(2, sink.AttackPressed);
        }

        [Test] // A2：按住中切 TOP（SetThirdPersonEnabled false）／切控制模式／回合作廢→Canceled 1，之後放開不送 Released
        public void A2_AttackHeld_ModeSwitchOrRoundCancel_CanceledOnce_LaterEndedSendsNothing()
        {
            for (int variant = 0; variant < 3; variant++)
            {
                TouchGestureRouter r = Make(out HoldSink sink, out LabActionButtonLayout layout);
                float x = Cx(layout.Attack), y = Cy(layout.Attack);
                r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Began, x, y, 0.0, 0.0); r.EndFrame();
                string label = variant == 0 ? "切 TOP" : variant == 1 ? "切控制模式" : "整批作廢";
                if (variant == 0) r.SetThirdPersonEnabled(false);
                else if (variant == 1) r.ActiveMode = ControlMode.ModeB_DualZonePip;
                else r.CancelActiveTouches();
                Assert.AreEqual(1, sink.AttackCanceled, label + "：送一次 Canceled");
                r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Stationary, x, y, 0.5, 0.0); r.EndFrame();
                r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Ended, x, y, 1.2, 0.0); r.EndFrame();
                Assert.AreEqual(0, sink.AttackReleased, label + "：作廢後的放開不送 Released");
                Assert.AreEqual(1, sink.AttackCanceled, label + "：Canceled 只送一次");
                Assert.AreEqual(1, sink.AttackPressed, label);
            }
        }

        [Test] // A2：DASH／WPN 鈕行為不變——按下送一次，放開／作廢都不送新通知
        public void A2_DashAndWeaponButtons_Unchanged_NoReleasedNoCanceled()
        {
            TouchGestureRouter r = Make(out HoldSink sink, out LabActionButtonLayout layout);
            float dx = Cx(layout.Dash), dy = Cy(layout.Dash), wx = Cx(layout.Weapon), wy = Cy(layout.Weapon);
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Began, dx, dy, 0.0, 0.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(1, TouchPhaseKind.Ended, dx, dy, 0.6, 0.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(2, TouchPhaseKind.Began, wx, wy, 1.0, 1.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(2, TouchPhaseKind.Ended, wx, wy, 1.6, 1.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(3, TouchPhaseKind.Began, dx, dy, 2.0, 2.0); r.EndFrame();
            r.BeginFrame(); r.ProcessTouch(3, TouchPhaseKind.Canceled, dx, dy, 2.1, 2.0); r.EndFrame();
            Assert.AreEqual(2, sink.DashPressed, "DASH 按下各送一次");
            Assert.AreEqual(1, sink.WeaponPressed, "WPN 按下送一次");
            Assert.AreEqual(0, sink.OtherReleased + sink.AttackReleased, "DASH／WPN 放開不送 Released");
            Assert.AreEqual(0, sink.OtherCanceled + sink.AttackCanceled, "DASH 觸控 Canceled 不送 Canceled");
            Assert.AreEqual(0, sink.AttackPressed + sink.Taps + sink.Flicks);
        }
    }
}
