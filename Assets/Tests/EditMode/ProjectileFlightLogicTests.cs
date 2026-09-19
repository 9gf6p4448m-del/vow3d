using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // 友軍彈道的掃掠排序與穿透記帳（PHASE2_BATCH3_PLAN.md §5 V2 d~f）。
    public sealed class ProjectileFlightLogicTests
    {
        private static SweepHit Hit(float distance, int id, SweepHitKind kind)
        {
            return new SweepHit { Distance = distance, Id = id, Kind = kind };
        }

        private static ProjectileFlightLogic NewFlight()
        {
            return new ProjectileFlightLogic(new ProjectileTuning().PenetratedWallBufferSize);
        }

        // V2-d：距離升冪；同距離取 Id 小者（決定性）；單筆與 0 筆不得炸；count 之後的內容不得被動到。
        [Test]
        public void SortByDistance_SortsAscending_BreaksTiesByLowerId_AndLeavesTheTailAlone()
        {
            SweepHit[] hits =
            {
                Hit(3f, 7, SweepHitKind.Target),
                Hit(1f, 5, SweepHitKind.FriendlyWall),
                Hit(2f, 9, SweepHitKind.BlockingWall)
            };
            ProjectileFlightLogic.SortByDistance(hits, 3);
            Assert.AreEqual(1f, hits[0].Distance, 1e-5f, "最近的必須排最前面");
            Assert.AreEqual(2f, hits[1].Distance, 1e-5f);
            Assert.AreEqual(3f, hits[2].Distance, 1e-5f);
            Assert.AreEqual(SweepHitKind.FriendlyWall, hits[0].Kind, "排序必須把整筆命中一起搬，不能只搬距離");

            SweepHit[] ties = { Hit(2f, 9, SweepHitKind.Target), Hit(2f, 4, SweepHitKind.FriendlyWall) };
            ProjectileFlightLogic.SortByDistance(ties, 2);
            Assert.AreEqual(4, ties[0].Id, "同距離必須取 Id 小者");
            Assert.AreEqual(9, ties[1].Id);

            SweepHit[] single = { Hit(5f, 1, SweepHitKind.Target) };
            ProjectileFlightLogic.SortByDistance(single, 1);
            Assert.AreEqual(1, single[0].Id);

            SweepHit[] tail = { Hit(3f, 3, SweepHitKind.Target), Hit(1f, 1, SweepHitKind.Target), Hit(99f, 99, SweepHitKind.Ignore) };
            ProjectileFlightLogic.SortByDistance(tail, 2); // 只排前 2 筆
            Assert.AreEqual(1, tail[0].Id);
            Assert.AreEqual(3, tail[1].Id);
            Assert.AreEqual(99, tail[2].Id, "count 之外的內容不得被動到");

            SweepHit[] empty = { Hit(7f, 7, SweepHitKind.Target) };
            ProjectileFlightLogic.SortByDistance(empty, 0);
            Assert.AreEqual(7, empty[0].Id, "count=0 時不得動任何東西，也不得丟例外");
        }

        // V2-e：同一發子彈對同一面牆只算一次；不同牆的倍率連乘（§4-8）。
        [Test]
        public void RecordPenetration_CountsEachWallOnce_AndMultipliesAcrossDifferentWalls()
        {
            ProjectileFlightLogic flight = NewFlight();
            Assert.AreEqual(1f, flight.DamageMultiplier, 1e-5f, "Reset 狀態的倍率必須是 1");
            Assert.IsFalse(flight.HasPenetrated(11));

            flight.RecordPenetration(11, 0.85f);
            Assert.IsTrue(flight.HasPenetrated(11), "穿過的牆必須被記住");
            flight.RecordPenetration(11, 0.85f); // 同一面牆第二次（子彈在牆內跨幀）
            Assert.AreEqual(0.85f, flight.DamageMultiplier, 1e-5f,
                "同一面牆只能乘一次倍率（0.85，不是 0.7225）");

            flight.RecordPenetration(22, 0.85f); // 另一面牆
            Assert.AreEqual(0.85f * 0.85f, flight.DamageMultiplier, 1e-5f,
                "不同牆的倍率必須連乘（0.7225），不是覆寫");
        }

        // 從池裡再取用的子彈必須是乾淨的：倍率回 1、名冊清空。
        [Test]
        public void Reset_ClearsTheMultiplierAndThePenetrationRecord()
        {
            ProjectileFlightLogic flight = NewFlight();
            flight.RecordPenetration(11, 0.85f);
            flight.RecordPenetration(22, 0.85f);
            Assert.IsTrue(flight.HasPenetrated(11), "前置條件：Reset 之前這一發確實記了兩面牆");
            Assert.AreEqual(0.85f * 0.85f, flight.DamageMultiplier, 1e-5f, "前置條件：Reset 之前倍率已經不是 1");

            flight.Reset();

            Assert.AreEqual(1f, flight.DamageMultiplier, 1e-5f);
            Assert.IsFalse(flight.HasPenetrated(11), "Reset 後不得還記得上一發穿過的牆");
            Assert.IsFalse(flight.HasPenetrated(22));
        }

        // V2-f：把 §4-5 的穿隧推導釘成測試——30fps 的單幀位移已經大過牆厚，點查詢必然漏穿。
        [Test]
        public void StepLength_AtThirtyFps_ExceedsTheWallThickness_ButAtOneTwentyFpsItDoesNot()
        {
            ProjectileTuning tuning = new ProjectileTuning();
            float wallThickness = new RuneTuning().WallThickness;

            float slowFrame = ProjectileFlightLogic.StepLength(tuning.BulletSpeed, 1f / 30f);
            float fastFrame = ProjectileFlightLogic.StepLength(tuning.BulletSpeed, 1f / 120f);

            Assert.AreEqual(tuning.BulletSpeed / 30f, slowFrame, 1e-4f);
            Assert.AreEqual(tuning.BulletSpeed / 120f, fastFrame, 1e-4f);
            Assert.Greater(slowFrame, wallThickness,
                "30fps 的單幀位移必須大於牆厚，否則 §4-5「必須掃掠」的推導不成立");
            Assert.Less(fastFrame, wallThickness,
                "120fps 的單幀位移必須小於牆厚（這條是上面那條的反面對照）");
        }
    }
}
