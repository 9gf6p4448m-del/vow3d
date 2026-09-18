using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 符印石牆的壽命與友軍穿透損耗（GDD §參-1、§參-2）。
    public sealed class RuneWallLogicTests
    {
        private static RuneWallLogic NewWall()
        {
            RuneWallLogic wall = new RuneWallLogic(new RuneTuning());
            wall.Activate();
            return wall;
        }

        [Test]
        public void Wall_LivesForFiveSeconds()
        {
            RuneWallLogic wall = NewWall();
            Assert.AreEqual(5f, wall.RemainingLifespan, 1e-6);

            wall.Tick(4.99f);
            Assert.IsTrue(wall.IsAlive, "4.99 秒時石牆必須還在");

            wall.Tick(0.01f);
            Assert.IsFalse(wall.IsAlive, "滿 5.0 秒石牆必須消失");
            Assert.AreEqual(0f, wall.RemainingLifespan);
        }

        [Test]
        public void Wall_BreaksWhenHealthRunsOut()
        {
            RuneWallLogic wall = NewWall();
            wall.ApplyDamage(299f);
            Assert.IsTrue(wall.IsAlive);
            wall.ApplyDamage(1f);
            Assert.IsFalse(wall.IsAlive);
        }

        [Test]
        public void Penetration_FirstFiveUndecayed_NextFiveAt85Percent_EachShotWearsTheWall()
        {
            RuneWallLogic wall = NewWall();

            for (int shot = 1; shot <= 10; shot++)
            {
                float healthBefore = wall.Health;
                float lifespanBefore = wall.RemainingLifespan;

                Assert.IsTrue(wall.TryPenetrate(out float multiplier), "第 " + shot + " 發必須放行");
                Assert.AreEqual(shot <= 5 ? 1f : 0.85f, multiplier, 1e-6, "第 " + shot + " 發的傷害倍率");
                Assert.AreEqual(shot, wall.PenetrationCount);

                if (shot < 10)
                {
                    Assert.AreEqual(healthBefore - 30f, wall.Health, 1e-3, "每發扣 10% 最大生命");
                    Assert.AreEqual(lifespanBefore - 0.5f, wall.RemainingLifespan, 1e-4, "每發扣 0.5 秒壽命");
                    Assert.IsTrue(wall.IsAlive);
                }
            }

            Assert.IsFalse(wall.IsAlive, "第 10 發穿透後碉堡必須崩解（杜絕水泥自閉陣地）");
            Assert.IsFalse(wall.TryPenetrate(out float _), "第 11 發不得放行");
            Assert.AreEqual(10, wall.PenetrationCount);
        }

        [Test]
        public void Penetration_CanKillTheWallEarly_ThroughLifespanLoss()
        {
            RuneWallLogic wall = NewWall();
            wall.Tick(4.2f); // 剩 0.8 秒

            Assert.IsTrue(wall.TryPenetrate(out float _));
            Assert.IsTrue(wall.IsAlive);
            Assert.IsTrue(wall.TryPenetrate(out float _));
            Assert.IsFalse(wall.IsAlive, "0.8 − 0.5 − 0.5 < 0：壽命被火力耗盡");
        }

        [Test]
        public void DeadWall_IgnoresEverything_UntilReactivated()
        {
            RuneWallLogic wall = NewWall();
            wall.Kill();

            wall.Tick(1f);
            wall.ApplyDamage(50f);
            Assert.IsFalse(wall.TryPenetrate(out float _));
            Assert.IsFalse(wall.IsAlive);

            wall.Activate();
            Assert.IsTrue(wall.IsAlive);
            Assert.AreEqual(300f, wall.Health);
            Assert.AreEqual(5f, wall.RemainingLifespan, 1e-6);
            Assert.AreEqual(0, wall.PenetrationCount, "從池裡重新取用的牆，穿透計數必須歸零");
        }
    }
}
