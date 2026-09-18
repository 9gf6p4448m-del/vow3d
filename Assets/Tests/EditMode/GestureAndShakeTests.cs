using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    public sealed class GestureAndShakeTests
    {
        [Test] // A12
        public void MillimetersToPixels_UsesPhysicalDpi_WithFallback()
        {
            Assert.AreEqual(63.39f, GestureMath.MillimetersToPixels(3.5f, 460f, 160f), 0.01f);
            Assert.AreEqual(135.83f, GestureMath.MillimetersToPixels(7.5f, 460f, 160f), 0.01f);

            // 同樣 3.5mm 在低 PPI 平板上像素更少——證明不是固定像素門檻
            Assert.AreEqual(36.38f, GestureMath.MillimetersToPixels(3.5f, 264f, 160f), 0.01f);

            // dpi 回報 0 或負值：走 fallback，半徑不得變 0
            Assert.AreEqual(22.05f, GestureMath.MillimetersToPixels(3.5f, 0f, 160f), 0.01f);
            Assert.AreEqual(22.05f, GestureMath.MillimetersToPixels(3.5f, -1f, 160f), 0.01f);

            Assert.AreEqual(3.5f, GestureMath.PixelsToMillimeters(63.39f, 460f, 160f), 0.001f);
        }

        [TestCase(20.0, 0.0)]   // A12
        [TestCase(25.0, 45.0)]
        [TestCase(-20.0, 0.0)]
        [TestCase(100.0, 90.0)]
        [TestCase(170.0, 180.0)]
        [TestCase(-100.0, -90.0)]
        [TestCase(-140.0, -135.0)]
        public void SnapToEightWay_SnapsToNearest45Degrees(double inputDegrees, double expectedDegrees)
        {
            double rad = inputDegrees * System.Math.PI / 180.0;
            float x = (float)(System.Math.Cos(rad) * 50.0);
            float y = (float)(System.Math.Sin(rad) * 50.0);

            Assert.IsTrue(GestureMath.SnapToEightWay(x, y, out float sx, out float sy));

            double exp = expectedDegrees * System.Math.PI / 180.0;
            Assert.AreEqual((float)System.Math.Cos(exp), sx, 1e-5f);
            Assert.AreEqual((float)System.Math.Sin(exp), sy, 1e-5f);
            Assert.AreEqual(1f, sx * sx + sy * sy, 1e-5f, "輸出須為單位向量");
        }

        [Test]
        public void SnapToEightWay_RejectsZeroVector()
        {
            Assert.IsFalse(GestureMath.SnapToEightWay(0f, 0f, out float _, out float _));
        }

        [Test] // A14
        public void EdgeDeadzone_RejectsLeftRightBottom_Within8px()
        {
            const float w = 2796f, h = 1290f, m = GestureMath.EdgeDeadzonePixels;

            Assert.IsTrue(GestureMath.IsInEdgeDeadzone(7.9f, 600f, w, h, m), "左緣");
            Assert.IsTrue(GestureMath.IsInEdgeDeadzone(w - 7.9f, 600f, w, h, m), "右緣");
            Assert.IsTrue(GestureMath.IsInEdgeDeadzone(1400f, 7.9f, w, h, m), "底緣");

            Assert.IsFalse(GestureMath.IsInEdgeDeadzone(8.1f, 600f, w, h, m));
            Assert.IsFalse(GestureMath.IsInEdgeDeadzone(w - 8.1f, 600f, w, h, m));
            Assert.IsFalse(GestureMath.IsInEdgeDeadzone(1400f, 8.1f, w, h, m));

            // 頂緣不設死區（GDD 只規定左右與底邊）
            Assert.IsFalse(GestureMath.IsInEdgeDeadzone(1400f, h - 1f, w, h, m));
        }

        [Test]
        public void Tracker_ShortTravel_IsTap()
        {
            FlickGestureTracker t = default;
            t.Begin(1, 500f, 500f, 0.0);
            Assert.AreEqual(GestureOutcome.None, t.Move(510f, 505f, 0.03, 63.4f, 0.25f));
            Assert.AreEqual(GestureOutcome.Tap, t.End(512f, 506f, 0.06, 63.4f, 0.25f));
        }

        [Test]
        public void Tracker_CrossingMinRadiusQuickly_FlicksOnceAtCrossing()
        {
            FlickGestureTracker t = default;
            t.Begin(1, 500f, 500f, 0.0);
            Assert.AreEqual(GestureOutcome.None, t.Move(540f, 503f, 0.02, 63.4f, 0.25f));
            Assert.AreEqual(GestureOutcome.Flick, t.Move(570f, 506f, 0.04, 63.4f, 0.25f), "越過半徑當下即判定，不等手指離開");
            Assert.AreEqual(1f, t.FlickDirX, 1e-5f);
            Assert.AreEqual(0f, t.FlickDirY, 1e-5f);

            // 同一次觸控之後劃再遠也不再產生第二次 flick，離手也不得補發 tap
            Assert.AreEqual(GestureOutcome.None, t.Move(900f, 506f, 0.06, 63.4f, 0.25f));
            Assert.AreEqual(GestureOutcome.None, t.End(900f, 506f, 0.08, 63.4f, 0.25f));
        }

        [Test]
        public void Tracker_FlickDetectedOnlyAtRelease_StillCounts()
        {
            FlickGestureTracker t = default;
            t.Begin(1, 500f, 500f, 0.0);
            Assert.AreEqual(GestureOutcome.Flick, t.End(500f, 420f, 0.03, 63.4f, 0.25f));
            Assert.AreEqual(-1f, t.FlickDirY, 1e-5f);
        }

        [Test]
        public void Tracker_SlowDrag_IsNeitherFlickNorTap()
        {
            FlickGestureTracker t = default;
            t.Begin(1, 500f, 500f, 0.0);
            Assert.AreEqual(GestureOutcome.None, t.Move(600f, 500f, 0.6, 63.4f, 0.25f));
            Assert.AreEqual(GestureOutcome.None, t.End(600f, 500f, 0.7, 63.4f, 0.25f));
        }

        [Test]
        public void Pip_OnlyYieldsDirection_BeyondMinRadius()
        {
            MicroVectorPip pip = default;
            pip.Begin(2, 200f, 200f);
            pip.Move(220f, 210f, 63.4f);
            Assert.IsFalse(pip.HasVector, "3.5mm 內為中立區");

            pip.Move(200f, 300f, 63.4f);
            Assert.IsTrue(pip.HasVector);
            Assert.AreEqual(0f, pip.DirX, 1e-5f);
            Assert.AreEqual(1f, pip.DirY, 1e-5f);

            pip.End();
            Assert.IsFalse(pip.HasVector);
            Assert.IsFalse(pip.Held);
        }

        [Test] // A13
        public void TraumaShake_IsQuadratic_DecaysToZero_AndKeepsHigherPriority()
        {
            TraumaShake shake = default;
            shake.Request(0.5f, 0.2f);
            Assert.AreEqual(0.25f, shake.Intensity, 1e-6f, "強度 = trauma²");

            shake.Request(0.2f, 0.2f); // 較輕的新請求不得壓低當前創傷
            Assert.AreEqual(0.5f, shake.Trauma, 1e-6f);

            shake.Request(0.8f, 0.4f); // 較重的請求覆蓋
            Assert.AreEqual(0.8f, shake.Trauma, 1e-6f);

            shake.Tick(0.2f);
            Assert.AreEqual(0.4f, shake.Trauma, 1e-5f, "0.4s 線性衰減，走到一半");
            Assert.AreEqual(0.16f, shake.Intensity, 1e-5f);

            shake.Tick(0.2f);
            Assert.AreEqual(0f, shake.Trauma, 1e-6f);
            shake.Tick(1f);
            Assert.AreEqual(0f, shake.Trauma, 0f, "不得衰減成負值");

            shake.Request(5f, 0.1f);
            Assert.AreEqual(1f, shake.Trauma, 1e-6f, "上限 1.0");
        }
    }
}
