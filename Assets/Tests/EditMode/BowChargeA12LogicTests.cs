using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 弓蓄力 追加 A12c（純邏輯）：「蓄力時停火」與快速射擊共用同一個 0.2s 門檻，邊界浮點正確。
    public sealed class BowChargeA12LogicTests
    {
        [Test]
        public void A12c_CeasefireThreshold_SameAsQuickShot_BoundaryExact()
        {
            Assert.AreEqual(0.2, BowChargeLogic.QuickShotSeconds, 0.0, "門檻 0.2s");
            Assert.IsFalse(BowChargeLogic.IsCharging(0.15), "0.15s：快速點擊，不停火");
            Assert.IsFalse(BowChargeLogic.IsCharging(0.1999), "0.1999s：快速點擊");
            Assert.IsTrue(BowChargeLogic.IsCharging(0.2), "0.2s 起：蓄力（停火）");
            Assert.IsTrue(BowChargeLogic.IsCharging(0.25), "0.25s：蓄力（停火）");
            Assert.IsFalse(BowChargeLogic.IsCharging(-1.0));
            Assert.IsFalse(BowChargeLogic.IsCharging(double.NaN));
            // 同源：IsCharging 與「不是快速射擊」逐點一致
            double[] ts = { 0.0, 0.1, 0.1999, 0.2, 0.2001, 0.5, 1.0, 3.0 };
            foreach (double t in ts)
                Assert.AreEqual(!BowChargeLogic.Resolve(t).IsQuick, BowChargeLogic.IsCharging(t), "t=" + t);
        }
    }
}
