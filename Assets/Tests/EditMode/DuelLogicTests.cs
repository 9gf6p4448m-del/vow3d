using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests
{
    public sealed class DuelLogicTests
    {
        [Test]
        public void ConfirmedTuning_HasTheApprovedValues()
        {
            var t = new DuelTuning();
            Assert.AreEqual(100f, t.HeroHealth);
            Assert.AreEqual(300f, t.OpponentHealth);
            Assert.AreEqual(20f, t.OpponentDamage);
            Assert.AreEqual(4f, t.OpponentMoveSpeed);
            Assert.AreEqual(1.8f, t.OpponentStopDistance);
            Assert.AreEqual(1.5f, t.AttackRadius);
            Assert.AreEqual(0.7f, t.WindupSeconds);
            Assert.AreEqual(1f, t.RecoverySeconds);
            Assert.AreEqual(2.5f, t.ResetDelaySeconds);
        }

        [Test]
        public void Round_OnlyStartsWhenDormant_AndResetsAfterTheFullPause()
        {
            var round = new DuelRoundLogic(new DuelTuning());
            Assert.IsTrue(round.TryStart());
            Assert.IsFalse(round.TryStart());
            Assert.AreEqual(1, round.StartCount);
            Assert.IsTrue(round.Knockout());
            Assert.IsFalse(round.Knockout());
            Assert.IsFalse(round.TryStart());
            Assert.IsFalse(round.Tick(2.49f));
            Assert.AreEqual(DuelRoundState.KnockoutPause, round.State);
            Assert.IsTrue(round.Tick(0.02f));
            Assert.IsFalse(round.Tick(1f));
            Assert.IsTrue(round.TryStart());
            Assert.AreEqual(2, round.StartCount);
        }

        [Test]
        public void Vitality_KnockoutFiresOnce_AndRestoreFillsHealth()
        {
            var health = new HeroVitality(100f);
            Assert.IsFalse(health.TakeDamage(20f));
            Assert.AreEqual(80f, health.Health);
            Assert.IsFalse(health.TakeDamage(0f));
            Assert.IsTrue(health.TakeDamage(80f));
            Assert.IsFalse(health.TakeDamage(20f));
            Assert.AreEqual(0f, health.Health);
            health.Restore();
            Assert.AreEqual(100f, health.Health);
        }

        [Test]
        public void ResetDuringWindup_DropsTheTarget_AndLateAnimationHitCannotDamageIt()
        {
            var h = new BrainTestHarness();
            var target = new FakeTarget();
            h.Brain.CommandAttack(target);
            Assert.AreEqual(PlayerState.AttackWindup, h.State);
            h.Brain.ResetForRound();
            h.Brain.NotifyAttackHit();
            h.Advance(0.5);
            Assert.AreEqual(PlayerState.Idle, h.State);
            Assert.IsNull(h.Brain.CurrentTarget);
            Assert.AreEqual(0, target.HitsTaken);
        }

        [Test]
        public void ClearingShield_RemovesTheOldRoundsProtection()
        {
            var shield = new RockShieldLogic(new ProjectileTuning());
            shield.Grant();
            shield.Clear();
            Assert.AreEqual(0f, shield.Amount);
            Assert.AreEqual(0f, shield.RemainingSeconds);
            Assert.AreEqual(20f, shield.Absorb(20f));
        }
    }
}
