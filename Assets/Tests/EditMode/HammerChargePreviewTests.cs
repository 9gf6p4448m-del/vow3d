using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 錘蓄力重擊 A3／A8（純邏輯部分）。凍結檔：vow-toolchain/acceptance-hammer-20261006.md（含修訂一）。
    // 只引用基底 26d0b66 已有的公開 API（WeaponAimPreview.For），基底編得過、紅在行為斷言。
    // 曲線：p＝t/1.2（t < 0.2s＝快速橫掃 p＝0），全角 100→130°、半徑 3.5→4.5m 隨 p 線性。
    public sealed class HammerChargePreviewTests
    {
        private const float Tol = 1e-3f;

        private static float ExpectedP(double t)
        {
            if (!(t >= 0.2)) return 0f;
            double p = t / 1.2;
            return p >= 1.0 ? 1f : (float)p;
        }

        [Test] // A8／A3：預覽扇形隨蓄力線性放大到蓄滿 130°／4.5m，快速門檻內＝現行 100°／3.5m
        public void HA8_HammerPreview_GrowsLinearlyToFullCharge()
        {
            double[] ts = { -0.5, 0.0, 0.1, 0.1999, 0.2, 0.6, 0.9, 1.2, 2.0 };
            foreach (double t in ts)
            {
                WeaponAimPreview pv = WeaponAimPreview.For(WeaponId.Hammer, t);
                float p = ExpectedP(t);
                string label = "t=" + t;
                Assert.AreEqual(WeaponPreviewKind.Sector, pv.Kind, label + " 形狀仍是扇形");
                Assert.AreEqual(100f + 30f * p, pv.FullAngleDegrees, Tol, label + " 全角＝100+30p");
                Assert.AreEqual(3.5f + 1.0f * p, pv.RangeMeters, Tol, label + " 半徑＝3.5+1.0p");
            }
            WeaponAimPreview full = WeaponAimPreview.For(WeaponId.Hammer, 1.2);
            Assert.AreEqual(130f, full.FullAngleDegrees, Tol, "蓄滿 130°");
            Assert.AreEqual(4.5f, full.RangeMeters, Tol, "蓄滿 4.5m");
        }

        [Test] // A3：0～2s 每 0.01s 取樣，全角與半徑單調不降，且蓄滿後確實比快速橫掃大
        public void HA3_HammerPreview_MonotoneNonDecreasing_AndActuallyGrows()
        {
            float prevAngle = -1f, prevRange = -1f;
            for (int i = 0; i <= 200; i++)
            {
                double t = i * 0.01;
                WeaponAimPreview pv = WeaponAimPreview.For(WeaponId.Hammer, t);
                Assert.GreaterOrEqual(pv.FullAngleDegrees, prevAngle - 1e-5f, "t=" + t + " 全角不得變小");
                Assert.GreaterOrEqual(pv.RangeMeters, prevRange - 1e-5f, "t=" + t + " 半徑不得變小");
                prevAngle = pv.FullAngleDegrees;
                prevRange = pv.RangeMeters;
            }
            WeaponAimPreview mid = WeaponAimPreview.For(WeaponId.Hammer, 0.6);
            Assert.Greater(mid.FullAngleDegrees, 100f + 1f, "中間值（0.6s）已放大，不是只在蓄滿那刻跳");
            Assert.Less(mid.FullAngleDegrees, 130f - 1f, "中間值（0.6s）未到蓄滿");
            Assert.AreEqual(130f, prevAngle, Tol, "最後取樣＝蓄滿全角");
            Assert.AreEqual(4.5f, prevRange, Tol, "最後取樣＝蓄滿半徑");
        }
    }
}
