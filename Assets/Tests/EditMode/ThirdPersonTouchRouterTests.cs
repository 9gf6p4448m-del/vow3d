using NUnit.Framework;
using Vow.Core;
using Vow.Input;

namespace Vow.Tests
{
    public sealed class ThirdPersonTouchRouterTests
    {
        private sealed class Sink : ITouchGestureSink
        {
            public int Taps, Flicks, Ui, Runes;
            public void OnWorldTap(float x, float y) { Taps++; }
            public void OnCadenceFlick(float x, float y) { Flicks++; }
            public void OnUiRegionTapped(int id) { Ui++; }
            public void OnRuneDragUpdated(float x, float y, float amount) { Runes++; }
            public void OnRuneQuickCast() { Runes++; }
            public void OnRuneReleased(float x, float y, float amount) { Runes++; }
            public void OnRuneCancelled() { Runes++; }
        }
        private static TouchGestureRouter Make(out Sink sink, out InputRoutingManager routing)
        {
            routing = new InputRoutingManager();
            routing.SetPipZone(new ScreenRegion(0, 0, 300, 200));
            routing.SetRuneZone(new ScreenRegion(740, 20, 820, 100));
            sink = new Sink();
            var router = new TouchGestureRouter(routing, sink, ControlMode.ModeB_DualZonePip)
            { ScreenWidth = 844, ScreenHeight = 390, MinRadiusPixels = 10, JoystickRadiusPixels = 50,
              MoveZone = new ScreenRegion(10, 10, 350, 200), RuneSaturationPixels = 50, RuneTapSlopPixels = 10 };
            router.SetThirdPersonEnabled(true);
            return router;
        }
        [Test]
        public void TwoFingers_MoveAndLook_OwnTheirStartRoutes_ReleaseNeverLeaks()
        {
            var r = Make(out Sink sink, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 100, 100, 0, 0);
            r.ProcessTouch(2, TouchPhaseKind.Began, 600, 200, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, 100, 150, .05, 0);
            r.ProcessTouch(2, TouchPhaseKind.Moved, 640, 230, .05, 0);
            Assert.AreEqual(1f, r.MoveY); Assert.AreEqual(0f, r.MoveX);
            Assert.AreEqual(40f, r.LookDeltaX); Assert.AreEqual(30f, r.LookDeltaY);
            r.ProcessTouch(2, TouchPhaseKind.Moved, 100, 100, .1, 0);
            Assert.AreEqual(1f, r.MoveY, "右指跨到搖桿不得偷走移動");
            r.ProcessTouch(1, TouchPhaseKind.Ended, 750, 50, .15, 0);
            r.ProcessTouch(2, TouchPhaseKind.Ended, 100, 100, .15, 0);
            Assert.IsFalse(r.MoveHeld); Assert.AreEqual(0, r.MoveY);
            Assert.AreEqual(0, sink.Taps); Assert.AreEqual(0, sink.Flicks); Assert.AreEqual(0, sink.Runes);
        }
        [Test]
        public void UiAndRune_WinOverMoveLook_AndDoNotLeak()
        {
            var r = Make(out Sink sink, out InputRoutingManager routing);
            routing.RegisterUiRegion(new ScreenRegion(50, 50, 150, 150));
            r.ProcessTouch(1, TouchPhaseKind.Began, 100, 100, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Ended, 100, 100, .1, 0);
            r.ProcessTouch(2, TouchPhaseKind.Began, 780, 60, 0, 0);
            r.ProcessTouch(2, TouchPhaseKind.Ended, 780, 60, .1, 0);
            Assert.AreEqual(1, sink.Ui); Assert.AreEqual(1, sink.Runes);
            Assert.AreEqual(0, sink.Taps); Assert.AreEqual(0, sink.Flicks); Assert.IsFalse(r.MoveHeld);
        }
        [Test]
        public void ShortRightTap_IsWorldTap_LongHoldAndDragAreNot()
        {
            var r = Make(out Sink sink, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 600, 200, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Ended, 602, 201, .1, 0);
            r.ProcessTouch(2, TouchPhaseKind.Began, 600, 200, 1, 1);
            r.ProcessTouch(2, TouchPhaseKind.Ended, 600, 200, 1.5, 1);
            r.ProcessTouch(3, TouchPhaseKind.Began, 600, 200, 2, 2);
            r.ProcessTouch(3, TouchPhaseKind.Ended, 650, 200, 2.1, 2);
            Assert.AreEqual(1, sink.Taps); Assert.AreEqual(0, sink.Flicks);
        }
        [TestCase(TouchPhaseKind.Canceled)]
        [TestCase(TouchPhaseKind.Ended)]
        public void EndOrCancel_StopsMove(TouchPhaseKind phase)
        {
            var r = Make(out Sink sink, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 100, 100, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, 200, 200, .1, 0);
            Assert.That(r.MoveX * r.MoveX + r.MoveY * r.MoveY, Is.EqualTo(1f).Within(.0001f));
            r.ProcessTouch(1, phase, 200, 200, .2, 0);
            Assert.AreEqual(0, r.MoveX); Assert.AreEqual(0, r.MoveY); Assert.AreEqual(0, sink.Taps);
        }
        [Test]
        public void LostTouchAndLifecycleInvalidation_ClearAndRejectHeldFinger()
        {
            var r = Make(out Sink sink, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 100, 100, 0, 0);
            r.ProcessTouch(1, TouchPhaseKind.Moved, 150, 100, .1, 0);
            r.BeginFrame(); r.EndFrame();
            Assert.AreEqual(0, r.MoveX); Assert.AreEqual(0, r.ActiveSlotCount);
            r.ProcessTouch(2, TouchPhaseKind.Began, 100, 100, 1, 1);
            r.ProcessTouch(3, TouchPhaseKind.Began, 600, 200, 1, 1);
            r.CancelActiveTouches();
            r.ProcessTouch(2, TouchPhaseKind.Moved, 200, 100, 1.1, 1);
            r.ProcessTouch(3, TouchPhaseKind.Moved, 680, 200, 1.1, 1);
            r.ProcessTouch(2, TouchPhaseKind.Ended, 200, 100, 1.2, 1);
            r.ProcessTouch(3, TouchPhaseKind.Ended, 680, 200, 1.2, 1);
            Assert.AreEqual(0, r.MoveX); Assert.AreEqual(0, r.LookDeltaX); Assert.AreEqual(0, sink.Taps);
        }
        [Test]
        public void SwitchBack_RestoresModeBWorldTapAndPip()
        {
            var r = Make(out Sink sink, out _);
            r.ProcessTouch(1, TouchPhaseKind.Began, 100, 100, 0, 0);
            r.SetThirdPersonEnabled(false);
            r.ProcessTouch(1, TouchPhaseKind.Ended, 100, 100, .1, 0);
            Assert.AreEqual(0, sink.Taps);
            r.ProcessTouch(2, TouchPhaseKind.Began, 600, 200, 1, 1);
            Assert.AreEqual(1, sink.Taps, "俯視ModeB恢復按下tap");
            r.ProcessTouch(3, TouchPhaseKind.Began, 100, 100, 1, 1);
            Assert.IsTrue(r.IsPipHeld); Assert.IsFalse(r.MoveHeld);
        }
    }
}
