using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 弓「蓄滿一條線」凍結驗收 C1（純邏輯）＋C3 的形狀切換（純邏輯部分）。凍結檔：vow-toolchain/acceptance-bowline-20261003.md。
    // dotnet 與 Unity EditMode 共用。刻意不直接引用新列舉值 WeaponPreviewKind.Line（以 ToString 比對），讓同一份測試在基底 223b876
    // 編得過、紅在行為斷言（C10）。
    public sealed class BowLineLogicTests
    {
        private const float Tol = 1e-4f;

        [Test] // C1：半角 p=0／0.5／1.0 → 12／7／2；t<0.2s（p=0）→ 12；線性
        public void C1_BowCone_TwelveToTwoDegrees_Linear()
        {
            Assert.AreEqual(12f, BowChargeLogic.Resolve(0.0).ConeHalfAngleDegrees, Tol, "p=0 → 12°");
            Assert.AreEqual(7f, BowChargeLogic.Resolve(0.5).ConeHalfAngleDegrees, Tol, "p=0.5 → 7°");
            Assert.AreEqual(2f, BowChargeLogic.Resolve(1.0).ConeHalfAngleDegrees, Tol, "p=1 → 2°");
            Assert.AreEqual(2f, BowChargeLogic.Resolve(3.0).ConeHalfAngleDegrees, Tol, "p 夾在 1 → 2°");
            Assert.AreEqual(12f, BowChargeLogic.Resolve(0.1).ConeHalfAngleDegrees, Tol, "t<0.2s 快速射擊 → 12°");
            Assert.AreEqual(12f, BowChargeLogic.Resolve(0.1999).ConeHalfAngleDegrees, Tol, "t=0.1999s 仍是快速射擊 → 12°");
            Assert.AreEqual(9.5f, BowChargeLogic.Resolve(0.25).ConeHalfAngleDegrees, Tol, "p=0.25 → 12−2.5（線性）");
            Assert.AreEqual(4.5f, BowChargeLogic.Resolve(0.75).ConeHalfAngleDegrees, Tol, "p=0.75 → 12−7.5（線性）");
            // 其餘數值沿用（規格：只改半角）
            BowShot full = BowChargeLogic.Resolve(1.0);
            Assert.AreEqual(16f, full.RangeMeters, Tol);
            Assert.AreEqual(1.8f, full.DamageMultiplier, Tol);
            Assert.IsTrue(full.Pierce);
        }

        [Test] // C3（純邏輯）：弓預覽 p<0.9＝Cone、p≥0.9＝Line，射程＝當下射程；其他武器不變
        public void C3_BowPreview_LineFromNinetyPercent()
        {
            Assert.AreEqual("Cone", WeaponAimPreview.For(WeaponId.Bow, 0.0).Kind.ToString(), "剛按下＝Cone");
            Assert.AreEqual("Cone", WeaponAimPreview.For(WeaponId.Bow, 0.5).Kind.ToString(), "p=0.5＝Cone");
            Assert.AreEqual("Cone", WeaponAimPreview.For(WeaponId.Bow, 0.899).Kind.ToString(), "p=0.899＝Cone");
            Assert.AreEqual("Line", WeaponAimPreview.For(WeaponId.Bow, 0.9).Kind.ToString(), "p=0.9＝Line");
            Assert.AreEqual("Line", WeaponAimPreview.For(WeaponId.Bow, 1.0).Kind.ToString(), "p=1＝Line");
            Assert.AreEqual("Line", WeaponAimPreview.For(WeaponId.Bow, 2.0).Kind.ToString(), "按更久仍是 Line");
            Assert.AreEqual(15.6f, WeaponAimPreview.For(WeaponId.Bow, 0.9).RangeMeters, Tol, "線長＝當下射程 12+4×0.9");
            Assert.AreEqual(16f, WeaponAimPreview.For(WeaponId.Bow, 1.0).RangeMeters, Tol);
            Assert.AreEqual("Cone", WeaponAimPreview.For(WeaponId.Grapple, 2.0).Kind.ToString(), "鉤鎖不受影響");
            Assert.AreEqual("Arc", WeaponAimPreview.For(WeaponId.Sword, 2.0).Kind.ToString(), "劍不受影響");
            Assert.AreEqual("Sector", WeaponAimPreview.For(WeaponId.Hammer, 2.0).Kind.ToString(), "錘不受影響");
            Assert.AreEqual("None", WeaponAimPreview.For(WeaponId.Standard, 2.0).Kind.ToString(), "Standard 不受影響");
        }

        // 修訂 R1 H2：弓錐 2°、目標半徑 0.5，「瞄點離中心 lateral 公尺、距離 d」時是否在錐內（＝半角＋atan(r/d)）。
        private static bool InBowCone(float halfDegrees, float d, float lateral, float radius)
        {
            AimTargetPicker picker = default;
            picker.Begin(0f, 0f, 0f, 1f, WeaponSpec.Bow, halfDegrees, 16f);
            return picker.Consider(0, lateral, (float)System.Math.Sqrt(d * d - lateral * lateral), false, true, radius);
        }

        [Test] // H2（純邏輯）：判定半角＝錐半角＋atan(半徑/距離)；radius 0＝原判定
        public void H2_BowCone_AddsTargetRadiusAngle()
        {
            float t3 = 14f * (float)System.Math.Sin(3.0 * System.Math.PI / 180.0);
            float t5 = 14f * (float)System.Math.Sin(5.0 * System.Math.PI / 180.0);
            Assert.IsTrue(InBowCone(2f, 14f, t3, 0.5f), "14m 偏 3°：2°＋2.05°≈4.05° > 3° → 中");
            Assert.IsFalse(InBowCone(2f, 14f, t5, 0.5f), "14m 偏 5° → 不中");
            Assert.IsTrue(InBowCone(2f, 3f, 0.30f, 0.5f), "3m 離中心 0.30m（身體內）→ 中");
            Assert.IsTrue(InBowCone(2f, 6f, 0.30f, 0.5f), "6m 離中心 0.30m → 中");
            Assert.IsFalse(InBowCone(2f, 3f, 0.80f, 0.5f), "3m 離中心 0.80m（身體外）→ 不中");
            Assert.IsFalse(InBowCone(2f, 6f, 0.80f, 0.5f), "6m 離中心 0.80m → 不中");
            Assert.IsFalse(InBowCone(2f, 14f, t3, 0f), "半徑 0＝原判定：3° > 2° 不中");
            Assert.IsTrue(InBowCone(12f, 6f, 6f * (float)System.Math.Sin(16.0 * System.Math.PI / 180.0), 0.5f), "快速射擊同式：12°＋4.76° > 16° → 中");
        }
    }
}
