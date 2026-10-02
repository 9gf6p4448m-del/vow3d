using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 弓蓄力凍結驗收 A1（純邏輯）＋A8 的形狀對照表（純邏輯部分）。凍結檔：vow-toolchain/acceptance-bowcharge-20261003.md。
    // dotnet 與 Unity EditMode 共用。本檔引用新型別 BowChargeLogic／WeaponAimPreview，基底 05f97b9 上編不過（A10 明文排除 A1）。
    public sealed class BowChargeLogicTests
    {
        private const float Tol = 1e-4f;

        private static void AssertShot(double t, float cone, float range, float multiplier, bool pierce)
        {
            BowShot s = BowChargeLogic.Resolve(t);
            string label = "t=" + t;
            Assert.AreEqual(cone, s.ConeHalfAngleDegrees, Tol, label + " 錐半角");
            Assert.AreEqual(range, s.RangeMeters, Tol, label + " 射程");
            Assert.AreEqual(multiplier, s.DamageMultiplier, Tol, label + " 傷害倍率");
            Assert.AreEqual(pierce, s.Pierce, label + " 穿透");
        }

        [Test] // A1：t=0.1／0.2／0.5／1.0／2.0、負 t
        public void A1_BowCharge_TableMatchesFrozenSpec()
        {
            AssertShot(0.1, 12f, 12f, 1f, false);      // < 0.2s：快速射擊，p＝0
            AssertShot(0.2, 10.4f, 12.8f, 1.16f, false); // 0.2s 起算蓄力：p＝0.2
            AssertShot(0.5, 8f, 14f, 1.4f, false);      // 線性（平方會是 p＝0.25）
            AssertShot(1.0, 4f, 16f, 1.8f, true);       // 滿蓄才穿透
            AssertShot(2.0, 4f, 16f, 1.8f, true);       // clamp 上限
            AssertShot(-0.5, 12f, 12f, 1f, false);      // 負 t＝快速射擊
        }

        [Test] // A1 補充：門檻兩側與 p 本身
        public void A1_BowCharge_QuickThresholdAndProgress()
        {
            Assert.AreEqual(0f, BowChargeLogic.Progress(0.1999), Tol, "0.1999s 仍是快速射擊");
            Assert.AreEqual(0.2f, BowChargeLogic.Progress(0.2), Tol, "0.2s 起 p＝t");
            Assert.AreEqual(0.75f, BowChargeLogic.Progress(0.75), Tol);
            Assert.AreEqual(1f, BowChargeLogic.Progress(5.0), Tol);
            Assert.IsTrue(BowChargeLogic.Resolve(0.1).IsQuick);
            Assert.IsFalse(BowChargeLogic.Resolve(0.2).IsQuick);
            Assert.IsFalse(BowChargeLogic.Resolve(0.999).Pierce, "差一點滿蓄不穿透");
            AssertShot(0.0, WeaponSpec.Bow.ConeHalfAngleDegrees, WeaponSpec.Bow.AimRangeMeters, 1f, false);
        }

        [Test] // A8（純邏輯部分）：五把武器的預覽形狀與參數
        public void A8_PreviewTable_FiveWeapons()
        {
            Assert.AreEqual(WeaponPreviewKind.None, WeaponAimPreview.For(WeaponId.Standard, 1.0).Kind, "Standard 無預覽");

            WeaponAimPreview sword = WeaponAimPreview.For(WeaponId.Sword, 1.0);
            Assert.AreEqual(WeaponPreviewKind.Arc, sword.Kind);
            Assert.AreEqual(5f, sword.RangeMeters, Tol);

            WeaponAimPreview hammer = WeaponAimPreview.For(WeaponId.Hammer, 1.0);
            Assert.AreEqual(WeaponPreviewKind.Sector, hammer.Kind);
            Assert.AreEqual(100f, hammer.FullAngleDegrees, Tol);
            Assert.AreEqual(3.5f, hammer.RangeMeters, Tol);

            WeaponAimPreview grapple = WeaponAimPreview.For(WeaponId.Grapple, 1.0);
            Assert.AreEqual(WeaponPreviewKind.Cone, grapple.Kind);
            Assert.AreEqual(20f, grapple.HalfAngleDegrees, Tol);
            Assert.AreEqual(10f, grapple.RangeMeters, Tol);

            WeaponAimPreview bow0 = WeaponAimPreview.For(WeaponId.Bow, 0.0);
            WeaponAimPreview bowHalf = WeaponAimPreview.For(WeaponId.Bow, 0.5);
            WeaponAimPreview bowFull = WeaponAimPreview.For(WeaponId.Bow, 1.0);
            Assert.AreEqual(WeaponPreviewKind.Cone, bow0.Kind);
            Assert.AreEqual(12f, bow0.HalfAngleDegrees, Tol);
            Assert.AreEqual(12f, bow0.RangeMeters, Tol);
            Assert.AreEqual(8f, bowHalf.HalfAngleDegrees, Tol);
            Assert.AreEqual(14f, bowHalf.RangeMeters, Tol);
            Assert.AreEqual(4f, bowFull.HalfAngleDegrees, Tol);
            Assert.AreEqual(16f, bowFull.RangeMeters, Tol);
        }
    }
}
