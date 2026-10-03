using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 弓「蓄滿一條線」覆審 r2 N1（凍結檔 acceptance-bowline-20261003.md「修訂 R2」；幾何見 acceptance-bowline-r2-interp.md）。
    // 只用 6bd257f 已有的 API（Consider 的 radiusMeters、ResolvedIndex），所以修前也編得過、紅在行為斷言。
    public sealed class BowLineR2LogicTests
    {
        private const float Body = 0.5f;   // 木樁半徑

        // 蓄滿（錐 2°、射程 16）：A＝英雄正在打的目標（preferred）在 nearD／夾角 sepDeg；B 在 10m、準星正對。回傳挑到誰（0＝A、1＝B、-1＝無）。
        private static int FullChargePick(float nearD, float sepDeg)
        {
            double r = sepDeg * System.Math.PI / 180.0;
            AimTargetPicker picker = default;
            // 準星正對 B（+z）；A 在準星右偏 sepDeg。
            picker.Begin(0f, 0f, 0f, 1f, WeaponSpec.Bow, 2f, 16f);
            picker.Consider(0, (float)(System.Math.Sin(r) * nearD), (float)(System.Math.Cos(r) * nearD), true, true, Body);
            picker.Consider(1, 0f, 10f, false, true, Body);
            return picker.ResolvedIndex;
        }

        [Test] // N1：黏性與「是否在錐內」用原錐半角；身體半徑不得讓近處正在打的 A 搶走精準瞄 B 的蓄滿箭（3m/10°、3m/6°、6m/6°）
        public void N1_FullChargePreciseAimAtB_StickyNearTargetDoesNotSteal()
        {
            Assert.AreEqual(1, FullChargePick(3f, 10f), "A 3m／10°：蓄滿精準瞄 B → 打 B");
            Assert.AreEqual(1, FullChargePick(3f, 6f), "A 3m／6°：蓄滿精準瞄 B → 打 B");
            Assert.AreEqual(1, FullChargePick(6f, 6f), "A 6m／6°：蓄滿精準瞄 B → 打 B");
        }

        [Test] // N1 對照（不放寬 R1 H2）：原錐內沒人時，線壓到身體仍中；黏性在原錐內照舊
        public void N1_BodyHitStillCountsWhenConeIsEmpty_StickyUnchangedInsideCone()
        {
            // 只有 A（preferred）在 3m、離中心 0.30m（5.71°，原錐 2° 外、身體內）→ 中 A
            AimTargetPicker p = default;
            p.Begin(0f, 0f, 0f, 1f, WeaponSpec.Bow, 2f, 16f);
            p.Consider(0, 0.30f, (float)System.Math.Sqrt(9.0 - 0.09), true, true, Body);
            Assert.AreEqual(0, p.ResolvedIndex, "原錐內沒人：身體判定仍中（R1 H2）");
            // 快速射擊錐 12°：A（preferred）3m／10° 在原錐內、B 10m／0° → 黏性保留 A（同基底 f9b4133）
            double r = 10.0 * System.Math.PI / 180.0;
            AimTargetPicker q = default;
            q.Begin(0f, 0f, 0f, 1f, WeaponSpec.Bow, 12f, 12f);
            q.Consider(0, (float)(System.Math.Sin(r) * 3.0), (float)(System.Math.Cos(r) * 3.0), true, true, Body);
            q.Consider(1, 0f, 10f, false, true, Body);
            Assert.AreEqual(0, q.ResolvedIndex, "原錐內的黏性不變");
        }
    }
}
