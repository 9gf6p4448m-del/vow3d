using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    public sealed class PactTalentModifierTests
    {
        [Test]
        public void V11B103_FireSurge_OnlyNewElementCastsUseFourPointTwoFiveSeconds()
        {
            var cooldowns = new ElementCastCooldowns(new ElementTuning());
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Water, 0f));
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Fire, 0f, 4.25f));
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Wind, 0f, 4.25f));
            Assert.AreEqual(5f, cooldowns.RemainingSeconds(ElementCast.Water, 0f));
            Assert.AreEqual(4.25f, cooldowns.RemainingSeconds(ElementCast.Fire, 0f));
            Assert.IsFalse(cooldowns.TryBeginCast(ElementCast.Fire, 4.249f, 4.25f));
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Fire, 4.25f, 4.25f));
            Assert.AreEqual(0.75f, cooldowns.RemainingSeconds(ElementCast.Water, 4.25f));
            Assert.AreEqual(0f, cooldowns.RemainingSeconds(ElementCast.Wind, 4.25f));
            cooldowns.ResetForRound();
            Assert.IsTrue(cooldowns.TryBeginCast(ElementCast.Water, 0f));
            Assert.AreEqual(5f, cooldowns.RemainingSeconds(ElementCast.Water, 0f));
        }

        [Test]
        public void V11B102_StoneBody_GrantOverrideDoesNotMutateDefaultShield()
        {
            var tuning = new ProjectileTuning();
            var shield = new RockShieldLogic(tuning);
            shield.Grant(220f);
            Assert.AreEqual(220f, shield.Amount);
            Assert.AreEqual(220f, shield.GrantedAmount);
            shield.Clear();
            shield.Grant();
            Assert.AreEqual(150f, shield.Amount);
            Assert.AreEqual(150f, shield.GrantedAmount);
            Assert.AreEqual(150f, tuning.ShieldAmount);
        }
    }
}
