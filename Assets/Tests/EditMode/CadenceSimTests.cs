using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    public sealed class CadenceSimTests
    {
        private const float Tick = 1f / 120f;
        private const float Eps = 1e-4f;

        private static CadenceSimInput DashInput(float x, float z)
        {
            return new CadenceSimInput { DashRequested = true, DirX = x, DirZ = z };
        }

        // 起手一次滑步並推進 seconds，回傳這段期間實際累積的位移長度與起手時宣告的距離。
        private static CadenceSimState DashAndAdvance(CadenceSimState s, CombatTuning t, float seconds,
            out float declared, out float traveled, out bool started)
        {
            float x = 0f, z = 0f;
            s = CadenceSim.SimulateStep(s, DashInput(1f, 0f), 0f, t, out CadenceSimStepResult r);
            started = r.DashStarted;
            declared = r.StartedDistance;

            int steps = (int)System.Math.Round(seconds / Tick);
            for (int i = 0; i < steps; i++)
            {
                s = CadenceSim.SimulateStep(s, default(CadenceSimInput), Tick, t, out r);
                x += r.DeltaX;
                z += r.DeltaZ;
            }
            traveled = (float)System.Math.Sqrt(x * x + z * z);
            return s;
        }

        [Test] // A1
        public void ThreeChainedDashes_Decay_1_4_Then_0_9_Then_0_5_Total_2_8()
        {
            CombatTuning t = new CombatTuning();
            CadenceSimState s = CadenceSimState.CreateFull(t);

            s = DashAndAdvance(s, t, 0.8f, out float d1, out float m1, out bool ok1);
            s = DashAndAdvance(s, t, 0.8f, out float d2, out float m2, out bool ok2);
            s = DashAndAdvance(s, t, 0.8f, out float d3, out float m3, out bool ok3);

            Assert.IsTrue(ok1 && ok2 && ok3);
            Assert.AreEqual(1.4f, d1, Eps);
            Assert.AreEqual(0.9f, d2, Eps);
            Assert.AreEqual(0.5f, d3, Eps);

            // 宣告距離與實際走出的距離必須一致（否則數字對了、身體卻沒走到）
            Assert.AreEqual(1.4f, m1, Eps);
            Assert.AreEqual(0.9f, m2, Eps);
            Assert.AreEqual(0.5f, m3, Eps);
            Assert.AreEqual(2.8f, m1 + m2 + m3, 3 * Eps);
        }

        [Test] // A2
        public void DashAfterChainWindowExpires_ResetsToFullDistance()
        {
            CombatTuning t = new CombatTuning();
            CadenceSimState s = CadenceSimState.CreateFull(t);

            s = DashAndAdvance(s, t, 1.2f, out float d1, out float _, out bool _);
            s = DashAndAdvance(s, t, 0.1f, out float d2, out float _, out bool _);

            Assert.AreEqual(1.4f, d1, Eps);
            Assert.AreEqual(1.4f, d2, Eps, "距上一次滑步已超過 1.0s，連段應重置");
        }

        [Test] // A2 邊界：窗口是「距上一次」的滾動窗口，不是從第一次起算
        public void ChainWindow_IsRollingFromPreviousDash()
        {
            CombatTuning t = new CombatTuning();
            CadenceSimState s = CadenceSimState.CreateFull(t);

            s = DashAndAdvance(s, t, 0.9f, out float _, out float _, out bool _);
            s = DashAndAdvance(s, t, 0.9f, out float d2, out float _, out bool _);
            s = DashAndAdvance(s, t, 0.1f, out float d3, out float _, out bool _); // 距第一次已 1.8s，但距第二次僅 0.9s

            Assert.AreEqual(0.9f, d2, Eps);
            Assert.AreEqual(0.5f, d3, Eps);
        }

        [Test] // A3
        public void Charges_CapAtThree_RejectAtZero_RecoverEvery2_5s()
        {
            CombatTuning t = new CombatTuning();
            CadenceSimState s = CadenceSimState.CreateFull(t);
            Assert.AreEqual(3, s.Charges);
            Assert.AreEqual(1f, CadenceSim.RecoveryNormalized(s, t), Eps);

            for (int i = 0; i < 3; i++) s = DashAndAdvance(s, t, 0.2f, out float _, out float _, out bool _);
            Assert.AreEqual(0, s.Charges);

            // 0 格：拒絕，且不得偷偷改動充能／連段／滑步狀態
            CadenceSimState before = s;
            s = CadenceSim.SimulateStep(s, DashInput(1f, 0f), 0f, t, out CadenceSimStepResult r);
            Assert.IsFalse(r.DashStarted);
            Assert.IsTrue(r.DashRejectedNoCharge);
            Assert.AreEqual(before.Charges, s.Charges);
            Assert.AreEqual(before.ChainCount, s.ChainCount);
            Assert.AreEqual(before.DashActive, s.DashActive);
            Assert.AreEqual(before.SinceLastDash, s.SinceLastDash);

            // 第一次滑步起手時開始回充；此時已過 0.6s，再 1.8s 還不到 2.5s
            for (int i = 0; i < 216; i++) s = CadenceSim.SimulateStep(s, default(CadenceSimInput), Tick, t, out r);
            Assert.AreEqual(0, s.Charges);
            float n = CadenceSim.RecoveryNormalized(s, t);
            Assert.IsTrue(n > 0.9f && n < 1f, "回充進度應接近滿：" + n);

            for (int i = 0; i < 24; i++) s = CadenceSim.SimulateStep(s, default(CadenceSimInput), Tick, t, out r);
            Assert.AreEqual(1, s.Charges);

            // 長時間閒置：回滿即止，不溢出
            for (int i = 0; i < 120 * 20; i++)
            {
                s = CadenceSim.SimulateStep(s, default(CadenceSimInput), Tick, t, out r);
                float k = CadenceSim.RecoveryNormalized(s, t);
                Assert.IsTrue(k >= 0f && k <= 1f);
                Assert.IsTrue(s.Charges <= t.MaxCharges);
            }
            Assert.AreEqual(3, s.Charges);
        }

        [Test] // A15
        public void SimulateStep_IsPureAndStepSizeIndependent()
        {
            CombatTuning t = new CombatTuning();
            CadenceSimState start = CadenceSimState.CreateFull(t);
            start = CadenceSim.SimulateStep(start, DashInput(0.6f, 0.8f), 0f, t, out CadenceSimStepResult _);

            // 同輸入同輸出
            CadenceSimState a = CadenceSim.SimulateStep(start, default(CadenceSimInput), 0.05f, t, out CadenceSimStepResult ra);
            CadenceSimState b = CadenceSim.SimulateStep(start, default(CadenceSimInput), 0.05f, t, out CadenceSimStepResult rb);
            Assert.AreEqual(a.DashTraveled, b.DashTraveled);
            Assert.AreEqual(ra.DeltaX, rb.DeltaX);
            Assert.AreEqual(ra.DeltaZ, rb.DeltaZ);

            // 一步 0.1s 與兩步 0.05s 的終點一致
            CadenceSimState one = CadenceSim.SimulateStep(start, default(CadenceSimInput), 0.1f, t, out CadenceSimStepResult r1);
            float oneX = r1.DeltaX, oneZ = r1.DeltaZ;

            CadenceSimState two = CadenceSim.SimulateStep(start, default(CadenceSimInput), 0.05f, t, out CadenceSimStepResult r2a);
            two = CadenceSim.SimulateStep(two, default(CadenceSimInput), 0.05f, t, out CadenceSimStepResult r2b);

            Assert.AreEqual(one.DashTraveled, two.DashTraveled, Eps);
            Assert.AreEqual(oneX, r2a.DeltaX + r2b.DeltaX, Eps);
            Assert.AreEqual(oneZ, r2a.DeltaZ + r2b.DeltaZ, Eps);
            Assert.IsTrue(one.DashTraveled > 0.5f, "0.1s 時滑步應已走過大半：" + one.DashTraveled);

            // 方向被正規化：位移長度不得隨輸入向量長度放大
            CadenceSimState big = CadenceSimState.CreateFull(t);
            big = CadenceSim.SimulateStep(big, DashInput(30f, 40f), 0f, t, out CadenceSimStepResult _);
            big = CadenceSim.SimulateStep(big, default(CadenceSimInput), 1f, t, out CadenceSimStepResult rBig);
            float len = (float)System.Math.Sqrt(rBig.DeltaX * rBig.DeltaX + rBig.DeltaZ * rBig.DeltaZ);
            Assert.AreEqual(1.4f, len, Eps);
        }

        [Test]
        public void ZeroDirection_DoesNotConsumeCharge()
        {
            CombatTuning t = new CombatTuning();
            CadenceSimState s = CadenceSimState.CreateFull(t);
            s = CadenceSim.SimulateStep(s, DashInput(0f, 0f), 0f, t, out CadenceSimStepResult r);
            Assert.IsFalse(r.DashStarted);
            Assert.AreEqual(3, s.Charges);
        }
    }
}
