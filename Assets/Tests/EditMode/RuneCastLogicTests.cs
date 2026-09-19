using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 符印施法的冷卻、落點換算與全隊石牆上限（GDD §參）。
    public sealed class RuneCastLogicTests
    {
        [Test]
        public void Cooldown_BlocksForEightSeconds_ThenReadyAgain()
        {
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());

            Assert.IsTrue(cast.IsReady(10.0));
            Assert.IsTrue(cast.TryBeginCast(10.0));
            Assert.IsFalse(cast.TryBeginCast(10.1), "冷卻中不得再次施放");
            Assert.IsFalse(cast.TryBeginCast(17.99));
            Assert.AreEqual(0.01, cast.CooldownRemaining(17.99), 1e-6);
            Assert.IsTrue(cast.TryBeginCast(18.0), "滿 8 秒必須可再次施放");
        }

        [Test]
        public void FailedCast_DoesNotExtendTheCooldown()
        {
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());
            cast.TryBeginCast(0.0);
            cast.TryBeginCast(7.0); // 冷卻中狂點
            Assert.IsTrue(cast.IsReady(8.0), "冷卻中的無效點擊不得把冷卻往後推");
        }

        [Test]
        public void WithoutTryBeginCast_NothingIsConsumed()
        {
            // 取消施法的路徑從不呼叫 TryBeginCast：查詢類方法不得有副作用
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());
            cast.IsReady(1.0);
            cast.CooldownRemaining(1.0);
            cast.TryDragPlacement(0f, 0f, 1f, 0f, 0.5f, out RuneWallPlacement _);
            cast.TryQuickCastPlacement(0f, 0f, 1f, 0f, out RuneWallPlacement _);
            Assert.IsTrue(cast.IsReady(1.0));
            Assert.AreEqual(0.0, cast.CooldownRemaining(1.0));
        }

        [Test]
        public void QuickCast_PlacesTheWallFourMetresAhead_FacingTheHero()
        {
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());

            // 朝向未正規化也要正確（transform.forward 投影到 XZ 後長度可能不是 1）
            Assert.IsTrue(cast.TryQuickCastPlacement(10f, -3f, 0f, 2f, out RuneWallPlacement p));
            Assert.AreEqual(10f, p.CenterX, 1e-5);
            Assert.AreEqual(1f, p.CenterZ, 1e-5);
            Assert.AreEqual(0f, p.NormalX, 1e-6);
            Assert.AreEqual(1f, p.NormalZ, 1e-6);
        }

        [Test]
        public void DragCast_MapsStretchToTwoThroughEightMetres()
        {
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());
            const float inv = 0.70710678f;

            Assert.IsTrue(cast.TryDragPlacement(1f, 1f, 1f, 1f, 0f, out RuneWallPlacement near));
            Assert.AreEqual(1f + 1.2f * inv, near.CenterX, 1e-4, "最近距離 1.2m（2026-09-19 使用者裁定，原 2m）");
            Assert.AreEqual(1f + 1.2f * inv, near.CenterZ, 1e-4);
            Assert.AreEqual(inv, near.NormalX, 1e-5);
            Assert.AreEqual(inv, near.NormalZ, 1e-5);

            Assert.IsTrue(cast.TryDragPlacement(0f, 0f, -1f, 0f, 1f, out RuneWallPlacement far));
            Assert.AreEqual(-8f, far.CenterX, 1e-5);
            Assert.AreEqual(0f, far.CenterZ, 1e-5);

            Assert.IsTrue(cast.TryDragPlacement(0f, 0f, 0f, 1f, 0.5f, out RuneWallPlacement mid));
            Assert.AreEqual(2.9f, mid.CenterZ, 1e-4, "0.5 已過貼身帶（1/3）：1.2 + 6.8 × (0.5 − 1/3) ÷ (2/3)");

            Assert.IsTrue(cast.TryDragPlacement(0f, 0f, 0f, 1f, 7f, out RuneWallPlacement clamped));
            Assert.AreEqual(8f, clamped.CenterZ, 1e-5, "拉伸量超出 0~1 要夾住，不得把牆丟到射程外");
        }

        // 2026-09-19 試玩回饋「想把牆放在身邊很容易觸發取消」：最近距離原本只對應「手指剛好停在取消圈邊緣」那一點。
        // 貼身帶：拖出取消圈後的前 1/3 行程（3.5～7mm）一律＝最近距離，之後才線性拉遠——貼身放牆離取消圈有一整圈緩衝。
        [Test]
        public void DragCast_NearBand_HoldsTheMinimumDistanceForTheFirstThirdOfTheStroke()
        {
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());

            float[] insideBand = { 0f, 0.1f, 0.2f, 0.33f };
            foreach (float t in insideBand)
            {
                Assert.IsTrue(cast.TryDragPlacement(0f, 0f, 0f, 1f, t, out RuneWallPlacement p));
                Assert.AreEqual(1.2f, p.CenterZ, 1e-4, "拉伸量 " + t + " 仍在貼身帶內");
            }

            Assert.IsTrue(cast.TryDragPlacement(0f, 0f, 0f, 1f, 2f / 3f, out RuneWallPlacement half));
            Assert.AreEqual(4.6f, half.CenterZ, 1e-4, "貼身帶之後線性：行程 2/3 處＝1.2 與 8 的中點");

            Assert.IsTrue(cast.TryDragPlacement(0f, 0f, 0f, 1f, 1f, out RuneWallPlacement far));
            Assert.AreEqual(8f, far.CenterZ, 1e-5);
        }

        [Test]
        public void Placement_RejectsZeroDirection()
        {
            RuneCastLogic cast = new RuneCastLogic(new RuneTuning());
            Assert.IsFalse(cast.TryQuickCastPlacement(0f, 0f, 0f, 0f, out RuneWallPlacement _));
            Assert.IsFalse(cast.TryDragPlacement(0f, 0f, 0f, 0f, 1f, out RuneWallPlacement _));
        }

        [Test]
        public void Roster_ThirdWallEvictsTheOldest()
        {
            RuneWallRoster roster = new RuneWallRoster(new RuneTuning().TeamWallCap);

            Assert.AreEqual(-1, roster.Add(0));
            Assert.AreEqual(-1, roster.Add(1));
            Assert.AreEqual(0, roster.Add(2), "場上已有 2 面，立第 3 面時最早的那面必須坍塌");
            Assert.AreEqual(2, roster.Count);
            Assert.AreEqual(1, roster.Add(0), "再立一面：輪到當時最早的那面");
        }

        [Test]
        public void Roster_ExpiredWallsFreeTheirSeat()
        {
            RuneWallRoster roster = new RuneWallRoster(2);
            roster.Add(0);
            roster.Add(1);

            roster.Remove(0); // 壽命到、自然消失
            Assert.AreEqual(1, roster.Count);
            Assert.AreEqual(-1, roster.Add(2), "已消失的牆不佔名額，不得誤拆還活著的牆");

            roster.Remove(7); // 不在名冊裡的 slot：無事發生
            Assert.AreEqual(2, roster.Count);
        }
    }
}
