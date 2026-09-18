using System.Collections.Generic;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    public sealed class LatencyAndHapticsTests
    {
        // ───────────────────────── D1 延遲佇列 ─────────────────────────

        [Test]
        public void Event_IsNeverDeliveredBeforeItsDelayElapses()
        {
            DelayedEventQueue<int> queue = new DelayedEventQueue<int>(8);
            queue.Enqueue(42, 10.000, 0.080, out int _);

            Assert.IsFalse(queue.TryDequeueDue(10.000, out int _));
            Assert.IsFalse(queue.TryDequeueDue(10.079, out int _), "79ms：還不到 80ms");
            Assert.IsTrue(queue.TryDequeueDue(10.080, out int value));
            Assert.AreEqual(42, value);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void Order_IsPreserved_EvenWhenALaterEventDrawsAShorterDelay()
        {
            DelayedEventQueue<int> queue = new DelayedEventQueue<int>(8);
            queue.Enqueue(1, 0.000, 0.080, out int _); // 到期 0.080
            queue.Enqueue(2, 0.010, 0.040, out int _); // 自己算是 0.050，但不得超車

            Assert.IsFalse(queue.TryDequeueDue(0.060, out int _), "第二筆不得在第一筆之前送出");

            List<int> delivered = new List<int>();
            while (queue.TryDequeueDue(0.080, out int v)) delivered.Add(v);
            CollectionAssert.AreEqual(new[] { 1, 2 }, delivered);
        }

        [Test]
        public void WhenFull_TheOldestEventIsHandedBack_NothingIsEverDropped()
        {
            DelayedEventQueue<int> queue = new DelayedEventQueue<int>(3);
            List<int> delivered = new List<int>();

            for (int i = 1; i <= 5; i++)
                if (queue.Enqueue(i, 0.0, 1.0, out int evicted)) delivered.Add(evicted);

            CollectionAssert.AreEqual(new[] { 1, 2 }, delivered, "滿了之後被擠出的是最舊的，且交還給呼叫端立即送出");
            while (queue.TryDequeueAny(out int v)) delivered.Add(v);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, delivered, "五筆一筆不少、順序不變");
        }

        [Test]
        public void ZeroDelay_IsDueImmediately()
        {
            DelayedEventQueue<int> queue = new DelayedEventQueue<int>(4);
            queue.Enqueue(7, 3.0, 0.0, out int _);
            Assert.IsTrue(queue.TryDequeueDue(3.0, out int value), "延遲 0 的事件在同一時刻就可取出");
            Assert.AreEqual(7, value);
        }

        [Test]
        public void Jitter_StaysInsideItsRange_AndIsReproducible()
        {
            LatencyJitter a = new LatencyJitter(12345u);
            LatencyJitter b = new LatencyJitter(12345u);
            double min = double.MaxValue, max = double.MinValue;

            for (int i = 0; i < 2000; i++)
            {
                double da = a.Next(0.045, 0.055);
                Assert.AreEqual(da, b.Next(0.045, 0.055), "同一顆種子必須產生同一串延遲");
                Assert.IsTrue(da >= 0.045 && da <= 0.055, "第 " + i + " 筆超出範圍：" + da);
                if (da < min) min = da;
                if (da > max) max = da;
            }

            Assert.IsTrue(max - min > 0.005, "抖動幾乎沒有變化，等於沒抖：" + min + " ~ " + max);
            Assert.AreEqual(0.05, new LatencyJitter(1u).Next(0.05, 0.05), "min == max 時回傳該值");
        }

        // ───────────────────────── D2 震覺策略 ─────────────────────────

        [Test]
        public void DashAndWallBreak_AreAlwaysHeavy_NeverThrottled()
        {
            HapticFatiguePolicy policy = HapticFatiguePolicy.CreateDefault();
            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(HapticStrength.Heavy, policy.Decide(HapticCue.CadenceDash, i * 0.1));
                Assert.AreEqual(HapticStrength.Heavy, policy.Decide(HapticCue.WallBreak, i * 0.1 + 0.05));
            }
        }

        [Test]
        public void BasicAttacks_BuzzLightlyThreeTimes_ThenGoQuiet_UntilThePlayerRests()
        {
            HapticFatiguePolicy policy = HapticFatiguePolicy.CreateDefault();
            double t = 0.0;

            for (int hit = 1; hit <= 3; hit++, t += 0.8)
                Assert.AreEqual(HapticStrength.Light, policy.Decide(HapticCue.BasicAttackHit, t), "第 " + hit + " 刀");
            for (int hit = 4; hit <= 12; hit++, t += 0.8)
                Assert.AreEqual(HapticStrength.None, policy.Decide(HapticCue.BasicAttackHit, t), "第 " + hit + " 刀應已靜音");

            // 中間夾著滑步（重震）不會重置普攻的疲勞計數
            Assert.AreEqual(HapticStrength.Heavy, policy.Decide(HapticCue.CadenceDash, t));
            Assert.AreEqual(HapticStrength.None, policy.Decide(HapticCue.BasicAttackHit, t + 0.8));

            // 門檻是「停手 ≥ 2.0s」。兩側都驗，且刻意不壓在 2.0 的浮點邊界上（0.8 的累加誤差會讓剛好 2.0 變成 1.9999…）
            t += 0.8 + 1.5;
            Assert.AreEqual(HapticStrength.None, policy.Decide(HapticCue.BasicAttackHit, t), "只停 1.5s：不得重置");
            t += 2.5;
            Assert.AreEqual(HapticStrength.Light, policy.Decide(HapticCue.BasicAttackHit, t), "停 2.5s：必須重置");
        }

        [Test]
        public void SilentMode_MutesBasicAttacks_ButNotTheDash()
        {
            HapticFatiguePolicy policy = HapticFatiguePolicy.CreateDefault();
            policy.BasicAttackMode = BasicAttackHaptics.Silent;

            Assert.AreEqual(HapticStrength.None, policy.Decide(HapticCue.BasicAttackHit, 0.0));
            Assert.AreEqual(HapticStrength.None, policy.Decide(HapticCue.BasicAttackHit, 5.0));
            Assert.AreEqual(HapticStrength.Heavy, policy.Decide(HapticCue.CadenceDash, 5.1));
        }
    }
}
