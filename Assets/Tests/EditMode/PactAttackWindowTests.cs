using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests.EditMode
{
    public sealed class PactAttackWindowTests
    {
        [Test]
        public void V11B201_MissDoesNotConsume_AndNextLandedHitDoes()
        {
            var window = new PactAttackWindow();
            window.Arm(10);
            Assert.IsTrue(window.IsArmed(10.5), "A missed swing never calls Consume");
            Assert.IsTrue(window.Consume(10.5));
            Assert.IsFalse(window.Consume(10.6), "Only the next landed basic attack receives the effect");
        }

        [Test]
        public void V11B201_ExpiryRefreshAndReset()
        {
            var window = new PactAttackWindow();
            window.Arm(10);
            Assert.IsFalse(window.Consume(11.001));
            window.Arm(12);
            window.Arm(12.5);
            Assert.IsTrue(window.Consume(13.5), "Second successful dash refreshes the window");
            window.Arm(20);
            window.Clear();
            Assert.IsFalse(window.Consume(20), "Knockout and match reset clear the window");
        }
    }
}
