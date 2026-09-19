using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 點擊挑選：跳過己方石牆、取最近的可接受命中（PHASE2_BATCH3_PLAN.md §5 V2-g、§4-1）。
    // 被跳過的條件只看命中自身，與順序無關，所以這裡不需要先排序。
    public sealed class TapPickLogicTests
    {
        // 己方牆最近、地板次之 → 要挑到地板（＝「點自家牆等於點到牆後的地板」這個手感）。
        // 敵方牆最近 → 要挑到敵方牆（＝鎖定去砸）。
        [Test]
        public void SelectNearestAcceptable_SkipsOwnWalls_AndTakesTheNearestOfTheRest()
        {
            float[] ownWallInFront = { 4.0f, 12.0f };
            bool[] ownWallFlags = { true, false };
            Assert.AreEqual(1, TapPickLogic.SelectNearestAcceptable(ownWallInFront, ownWallFlags, 2),
                "己方牆必須被跳過，改挑它後面的地板");

            float[] enemyWallInFront = { 4.0f, 12.0f };
            bool[] enemyWallFlags = { false, false };
            Assert.AreEqual(0, TapPickLogic.SelectNearestAcceptable(enemyWallInFront, enemyWallFlags, 2),
                "敵方／中立牆不得被跳過，它比地板近就該挑它");

            // 跳過之後要取「剩下的裡面最近的」，不是最遠的
            float[] threeHits = { 2.0f, 20.0f, 7.0f };
            bool[] threeFlags = { true, false, false };
            Assert.AreEqual(2, TapPickLogic.SelectNearestAcceptable(threeHits, threeFlags, 3),
                "跳過己方牆之後必須取最近的那一個（7m），不是最遠的（20m）");
        }

        [Test]
        public void SelectNearestAcceptable_ReturnsMinusOne_WhenEverythingIsSkippedOrThereIsNothing()
        {
            float[] distances = { 3.0f, 9.0f };
            bool[] allOwnWalls = { true, true };
            Assert.AreEqual(-1, TapPickLogic.SelectNearestAcceptable(distances, allOwnWalls, 2),
                "全部都是己方牆時必須回 -1（這一下什麼都不做）");

            bool[] noneSkipped = { false, false };
            Assert.AreEqual(-1, TapPickLogic.SelectNearestAcceptable(distances, noneSkipped, 0),
                "命中數 0 必須回 -1");
        }

        [Test]
        public void SelectNearestAcceptable_OnEqualDistances_TakesTheLowerIndex()
        {
            float[] distances = { 8.0f, 3.0f, 3.0f, 15.0f };
            bool[] ownWall = { false, false, false, false };
            Assert.AreEqual(1, TapPickLogic.SelectNearestAcceptable(distances, ownWall, 4),
                "同距離必須取索引小者（決定性）");

            float[] tieWithOwnWall = { 8.0f, 3.0f, 3.0f, 15.0f };
            bool[] firstTieIsOwnWall = { false, true, false, false };
            Assert.AreEqual(2, TapPickLogic.SelectNearestAcceptable(tieWithOwnWall, firstTieIsOwnWall, 4),
                "平手的兩個之中若索引小者是己方牆，要挑另一個，不是退回更遠的");
        }
    }
}
