using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 錘蓄力重擊純邏輯（HammerChargeLogic）。凍結檔：vow-toolchain/acceptance-hammer-20261006.md（含修訂一）。
    // 本檔引用新型別 HammerChargeLogic／HammerStrike，基底 26d0b66 上編不過——不計入基底紅燈；鑑別力以突變證明。
    // 基底可編、紅在行為的同組條件見 HammerChargePreviewTests（EditMode）與 HammerChargePlayTests（PlayMode）。
    public sealed class HammerChargeLogicTests
    {
        private const float Tol = 1e-4f;

        private static void AssertStrike(double t, float p, float angle, float range, float multiplier, float cooldown)
        {
            HammerStrike s = HammerChargeLogic.Resolve(t);
            string label = "t=" + t;
            Assert.AreEqual(p, s.Progress, Tol, label + " p");
            Assert.AreEqual(angle, s.FullAngleDegrees, Tol, label + " 全角");
            Assert.AreEqual(range, s.RangeMeters, Tol, label + " 半徑");
            Assert.AreEqual(multiplier, s.DamageMultiplier, Tol, label + " 傷害倍率");
            Assert.AreEqual(cooldown, s.CooldownSeconds, Tol, label + " 冷卻");
            Assert.AreEqual(0.25f, s.WindupSeconds, Tol, label + " 前搖");
        }

        [Test] // A1／A2／A3／A7：表列值（快速＝現行橫掃；中間線性；蓄滿 130°／4.5m／1.6 倍／1.2s）
        public void HL_HammerStrike_TableMatchesFrozenSpec()
        {
            AssertStrike(-0.5, 0f, 100f, 3.5f, 1f, 0.8f);
            AssertStrike(0.0, 0f, 100f, 3.5f, 1f, 0.8f);
            AssertStrike(0.1999, 0f, 100f, 3.5f, 1f, 0.8f);
            AssertStrike(0.6, 0.5f, 115f, 4.0f, 1.3f, 1.0f);
            AssertStrike(1.2, 1f, 130f, 4.5f, 1.6f, 1.2f);
            AssertStrike(3.0, 1f, 130f, 4.5f, 1.6f, 1.2f);
            Assert.IsTrue(HammerChargeLogic.Resolve(0.1).IsQuick);
            Assert.IsFalse(HammerChargeLogic.Resolve(0.2).IsQuick);
            Assert.AreEqual(0f, HammerChargeLogic.Progress(double.NaN), Tol, "NaN＝快速橫掃");
        }

        [Test] // A3：0～2s 每 0.01s 取樣，範圍、傷害、冷卻單調不降
        public void HL_HammerStrike_MonotoneNonDecreasing()
        {
            HammerStrike prev = HammerChargeLogic.Resolve(0.0);
            for (int i = 1; i <= 200; i++)
            {
                double t = i * 0.01;
                HammerStrike s = HammerChargeLogic.Resolve(t);
                Assert.GreaterOrEqual(s.FullAngleDegrees, prev.FullAngleDegrees - 1e-5f, "t=" + t + " 全角");
                Assert.GreaterOrEqual(s.RangeMeters, prev.RangeMeters - 1e-5f, "t=" + t + " 半徑");
                Assert.GreaterOrEqual(s.DamageMultiplier, prev.DamageMultiplier - 1e-5f, "t=" + t + " 傷害倍率");
                Assert.GreaterOrEqual(s.CooldownSeconds, prev.CooldownSeconds - 1e-5f, "t=" + t + " 冷卻");
                prev = s;
            }
        }

        [Test] // A2：蓄滿扇形 130°／4.5m（全角語意），快速橫掃仍 100°／3.5m
        public void HL_HammerStrike_SectorContainment()
        {
            HammerStrike full = HammerChargeLogic.Resolve(1.2);
            HammerStrike quick = HammerChargeLogic.Resolve(0.0);
            Assert.IsTrue(WeaponSweep.Contains(full, 0f, 0f, 0f, 1f, 0f, 4.4f), "蓄滿 4.4m 在內");
            Assert.IsFalse(WeaponSweep.Contains(full, 0f, 0f, 0f, 1f, 0f, 4.6f), "蓄滿 4.6m 在外");
            Assert.IsFalse(WeaponSweep.Contains(quick, 0f, 0f, 0f, 1f, 0f, 4.4f), "快速 4.4m 在外");
            float r = 60f * (float)System.Math.PI / 180f, r2 = 70f * (float)System.Math.PI / 180f;
            Assert.IsTrue(WeaponSweep.Contains(full, 0f, 0f, 0f, 1f, (float)System.Math.Sin(r) * 3f, (float)System.Math.Cos(r) * 3f), "蓄滿 60° 在內");
            Assert.IsFalse(WeaponSweep.Contains(full, 0f, 0f, 0f, 1f, (float)System.Math.Sin(r2) * 3f, (float)System.Math.Cos(r2) * 3f), "蓄滿 70° 在外");
            Assert.IsFalse(WeaponSweep.Contains(quick, 0f, 0f, 0f, 1f, (float)System.Math.Sin(r) * 3f, (float)System.Math.Cos(r) * 3f), "快速 60° 在外");
        }

        [Test] // WeaponSpec 數值登記（暫定、試玩即改）：步速 60%、收招 0.15s
        public void HL_WeaponSpec_HammerChargeConstants()
        {
            Assert.AreEqual(0.6f, WeaponSpec.HammerChargeMoveSpeedMultiplier, Tol);
            Assert.AreEqual(0.15f, WeaponSpec.HammerRecoverySeconds, Tol);
            Assert.AreEqual(0.2, WeaponSpec.HammerQuickSweepSeconds, 1e-9);
            Assert.AreEqual(1.2, WeaponSpec.HammerFullChargeSeconds, 1e-9);
        }
    }
}
