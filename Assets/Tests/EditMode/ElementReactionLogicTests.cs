using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-f～V2-o、V2-af：三大反應的判定、優先序、陣營方向。
    // 陣營一律 int 代碼：Core/Logic 不得引用 Contracts 的 Faction。這裡的數字只需要「互不相同」，
    // 判定式從不比對特定數值（IsHostile／同陣營比較除外，那兩條專門測數值本身）。
    public sealed class ElementReactionLogicTests
    {
        private const float Epsilon = 1e-4f;
        private const int Blue = 0;
        private const int Red = 1;

        private static void PointAtAngle(float originX, float originZ, float angleDegrees, float distance,
                                          out float x, out float z)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            x = (float)(originX + Math.Cos(rad) * distance);
            z = (float)(originZ + Math.Sin(rad) * distance);
        }

        // V2-f Rock_LandingInsideWater_MakesQuicksand_AndConsumesTheWater
        [Test]
        public void Rock_LandingInsideWater_MakesQuicksand_AndConsumesTheWater()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int waterId = field.Spawn(ElementZoneKind.Water, 5f, 5f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);

            // 距圓心約 2.55m（< WaterRadius 3），落在水域內
            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Rock, Blue, 7.1f, 6.4f, 0f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.Quicksand, outcome.Reaction);
            Assert.AreEqual(waterId, outcome.ConsumedZoneId);
            Assert.AreEqual((int)ElementZoneKind.Quicksand, outcome.NewZoneKindCode);
            Assert.AreEqual(tuning.ReactionRadius, outcome.NewZoneRadius, Epsilon);
            Assert.AreEqual(tuning.QuicksandDurationSeconds, outcome.NewZoneDurationSeconds, Epsilon);
            Assert.IsTrue(outcome.IsCombo);
            Assert.IsFalse(field.TryGetById(waterId, out _), "水域必須被消耗");
        }

        // V2-g Rock_LandingZeroPointOneMetreOutsideWater_MakesNothing
        [Test]
        public void Rock_LandingZeroPointOneMetreOutsideWater_MakesNothing()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int waterId = field.Spawn(ElementZoneKind.Water, 5f, 5f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);
            int before = field.ActiveCount;

            // 固定距圓心 3.100m（不隨 tuning.WaterRadius 漂移，用來咬住「剛好 0.1m 外」這個邊界）
            PointAtAngle(5f, 5f, 23.5f, 3.100f, out float landX, out float landZ);
            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Rock, Blue, landX, landZ, 0f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.None, outcome.Reaction);
            Assert.IsTrue(field.TryGetById(waterId, out _), "水域仍在");
            Assert.AreEqual(before, field.ActiveCount, "ActiveCount 不變");
        }

        // V2-h Wind_SweepingABurningZone_MakesAFirestorm_AndConsumesTheBurningZone（含反例，活性）
        [Test]
        public void Wind_SweepingABurningZone_MakesAFirestorm_AndConsumesTheBurningZone()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            const float facingDegrees = 37f;
            float dirX = (float)Math.Cos(facingDegrees * Math.PI / 180.0);
            float dirZ = (float)Math.Sin(facingDegrees * Math.PI / 180.0);

            // 活性：先證明掃到真的造成傷害＋消耗
            PointAtAngle(0f, 0f, facingDegrees + 12f, 4.5f, out float insideX, out float insideZ);
            int burningInside = field.Spawn(ElementZoneKind.Burning, insideX, insideZ, tuning.BurnRadius, tuning.BurnDurationSeconds, Blue);

            ReactionOutcome hit = ElementReactionLogic.Resolve(ElementCast.Wind, Blue, 0f, 0f, dirX, dirZ, field, tuning);
            Assert.AreEqual(ElementReaction.Firestorm, hit.Reaction);
            Assert.AreEqual(60f, hit.AoeDamage, 0.01f, "40 × 1.5 == 60");
            Assert.IsTrue(hit.IsCombo);
            Assert.IsFalse(field.TryGetById(burningInside, out _), "掃到的燃燒區必須被消耗");

            // 反例：同一個扇形，角外 1°
            PointAtAngle(0f, 0f, facingDegrees + 31f, 4.5f, out float outsideX, out float outsideZ);
            int burningOutside = field.Spawn(ElementZoneKind.Burning, outsideX, outsideZ, tuning.BurnRadius, tuning.BurnDurationSeconds, Blue);

            ReactionOutcome miss = ElementReactionLogic.Resolve(ElementCast.Wind, Blue, 0f, 0f, dirX, dirZ, field, tuning);
            Assert.AreEqual(ElementReaction.None, miss.Reaction);
            Assert.AreEqual(0f, miss.AoeDamage, 0.01f);
            Assert.IsTrue(field.TryGetById(burningOutside, out _), "角外的燃燒區必須還在");
        }

        // V2-i Fire_HittingWater_MakesSteam_AndLeavesNoBurningZone
        [Test]
        public void Fire_HittingWater_MakesSteam_AndLeavesNoBurningZone()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int waterId = field.Spawn(ElementZoneKind.Water, 0f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, Blue);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Fire, Blue, 0f, 0f, 1f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.Steam, outcome.Reaction);
            Assert.AreEqual(waterId, outcome.ConsumedZoneId);
            Assert.AreEqual((int)ElementZoneKind.Steam, outcome.NewZoneKindCode, "新區不得是 Burning");
            Assert.AreEqual(tuning.ReactionRadius, outcome.NewZoneRadius, Epsilon);
            Assert.AreEqual(tuning.SteamDurationSeconds, outcome.NewZoneDurationSeconds, Epsilon);
            Assert.AreEqual(0f, outcome.AoeDamage, 0.01f);
            Assert.IsTrue(outcome.IsCombo);
            Assert.IsFalse(field.TryGetById(waterId, out _), "水域必須被消耗");
            Assert.AreEqual(1, field.ActiveCount, "新區只有一個");
        }

        // V2-j Fire_HittingHostileQuicksand_Boils_AndTerminatesTheQuicksand（陣營方向 B：火陣營 ＝ 流沙陣營）
        [Test]
        public void Fire_HittingHostileQuicksand_Boils_AndTerminatesTheQuicksand()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int quicksandId = field.Spawn(ElementZoneKind.Quicksand, 0f, 0f, tuning.ReactionRadius, tuning.QuicksandDurationSeconds, Red);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Fire, Red, 0f, 0f, 1f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.Boil, outcome.Reaction);
            Assert.AreEqual(80f, outcome.AoeDamage, 0.01f);
            Assert.AreEqual(Red, outcome.DamageFactionId);
            Assert.AreEqual(0, outcome.NewZoneKindCode);
            Assert.IsFalse(field.TryGetById(quicksandId, out _), "流沙必須被終止");
        }

        // V2-k Fire_HittingFriendlyQuicksand_Rescues_AndTerminatesTheQuicksand（陣營方向 A：火陣營 ≠ 流沙陣營）
        [Test]
        public void Fire_HittingFriendlyQuicksand_Rescues_AndTerminatesTheQuicksand()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int quicksandId = field.Spawn(ElementZoneKind.Quicksand, 0f, 0f, tuning.ReactionRadius, tuning.QuicksandDurationSeconds, Red);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Fire, Blue, 0f, 0f, 1f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.Rescue, outcome.Reaction);
            Assert.AreEqual(0f, outcome.AoeDamage, 0.01f);
            Assert.IsFalse(field.TryGetById(quicksandId, out _), "流沙必須被終止（解控）");
        }

        // V2-l Fire_OnOpenGround_DealsDirectDamage_AndLeavesABurningZone
        [Test]
        public void Fire_OnOpenGround_DealsDirectDamage_AndLeavesABurningZone()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Fire, Blue, 2f, 3f, 1f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.PlainFire, outcome.Reaction);
            Assert.AreEqual(40f, outcome.AoeDamage, 0.01f);
            Assert.AreEqual((int)ElementZoneKind.Burning, outcome.NewZoneKindCode);
            Assert.AreEqual(tuning.BurnRadius, outcome.NewZoneRadius, Epsilon);
            Assert.AreEqual(tuning.BurnDurationSeconds, outcome.NewZoneDurationSeconds, Epsilon);
            Assert.AreEqual(-1, outcome.ConsumedZoneId);
            Assert.IsFalse(outcome.IsCombo, "空地火不得觸發 Combo");
            Assert.AreEqual(1, field.ActiveCount);
        }

        // V2-m Fire_OverlappingQuicksandAndWater_PicksQuicksandOnly
        [Test]
        public void Fire_OverlappingQuicksandAndWater_PicksQuicksandOnly()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int quicksandId = field.Spawn(ElementZoneKind.Quicksand, 0f, 0f, tuning.ReactionRadius, tuning.QuicksandDurationSeconds, Red);
            int waterId = field.Spawn(ElementZoneKind.Water, 0f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Fire, Red, 0f, 0f, 1f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.Boil, outcome.Reaction, "同時蓋到流沙與水域，只算流沙");
            Assert.IsFalse(field.TryGetById(quicksandId, out _));
            Assert.IsTrue(field.TryGetById(waterId, out _), "水域不該被消耗");
        }

        // V2-n Fire_OverlappingTwoWaterZones_PicksTheNearestCentre
        [Test]
        public void Fire_OverlappingTwoWaterZones_PicksTheNearestCentre()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            // 刻意讓「較遠」的那個先 Spawn（先佔到較小的 slot／較小的 id）：如果實作退化成
            // 「回傳掃描時第一個命中」而不是真的比距離，這個排列會讓它選到錯的（較遠）那個。
            int fartherWaterId = field.Spawn(ElementZoneKind.Water, 2.4f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);
            int nearerWaterId = field.Spawn(ElementZoneKind.Water, 1.2f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Fire, Blue, 0f, 0f, 1f, 0f, field, tuning);

            Assert.AreEqual(ElementReaction.Steam, outcome.Reaction);
            Assert.AreEqual(nearerWaterId, outcome.ConsumedZoneId, "必須消耗圓心較近的那個");
            Assert.IsTrue(field.TryGetById(fartherWaterId, out _), "較遠的水域不該被動到");

            // 平手：距離相等時取 id 小者（先 Spawn 的 id 較小）
            ElementZoneField tieField = new ElementZoneField(tuning);
            int firstWaterId = tieField.Spawn(ElementZoneKind.Water, 1.5f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);
            int secondWaterId = tieField.Spawn(ElementZoneKind.Water, -1.5f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, Red);

            ReactionOutcome tieOutcome = ElementReactionLogic.Resolve(ElementCast.Fire, Blue, 0f, 0f, 1f, 0f, tieField, tuning);
            Assert.AreEqual(firstWaterId, tieOutcome.ConsumedZoneId, "距離相等時取 id 較小者");
            Assert.Less(firstWaterId, secondWaterId);
        }

        // V2-o Quicksand_TakesTheFactionOfTheWall_WhenWaterAndWallDisagree
        [Test]
        public void Quicksand_TakesTheFactionOfTheWall_WhenWaterAndWallDisagree()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            field.Spawn(ElementZoneKind.Water, 0f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, 1);

            ReactionOutcome outcome = ElementReactionLogic.Resolve(ElementCast.Rock, 2, 0f, 0f, 0f, 0f, field, tuning);

            Assert.AreEqual(2, outcome.NewZoneFactionId, "流沙必須取岩（石牆）自己的陣營，不是水域的陣營");
        }

        // V2-af IsHostile_IsTrueAcrossFactions_AndFalseWithinOne
        [Test]
        public void IsHostile_IsTrueAcrossFactions_AndFalseWithinOne()
        {
            Assert.IsTrue(ElementReactionLogic.IsHostile(1, 2));
            Assert.IsFalse(ElementReactionLogic.IsHostile(1, 1));
            Assert.IsTrue(ElementReactionLogic.IsHostile(2, 1));
        }
    }
}
