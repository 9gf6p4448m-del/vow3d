namespace Vow.Core.Logic
{
    public enum ElementCast { Water = 1, Fire = 2, Wind = 3, Rock = 4 }

    public enum ElementReaction
    {
        None = 0, Quicksand = 1, Steam = 2, Boil = 3, Rescue = 4,
        Firestorm = 5, PlainFire = 6, WaterPool = 7
    }

    public struct ReactionOutcome
    {
        public ElementReaction Reaction;
        public int ConsumedZoneId;      // 被消耗／終止的區域 id；無 ＝ -1
        public int NewZoneKindCode;     // 要新生的區域（ElementZoneKind），無 ＝ 0
        public float NewZoneRadius, NewZoneDurationSeconds;
        public int NewZoneFactionId;
        public float AoeDamage;         // 對「與 DamageFactionId 敵對」的目標；無傷害 ＝ 0
        public int DamageFactionId;
        public bool IsCombo;            // true → 呼叫端做 Hitstop 45ms ＋ 震屏
    }

    // 三大元素反應的唯一判定入口（PHASE2_BATCH4_PLAN.md §2、§4）。零 UnityEngine，陣營一律 int 代碼。
    // 這個函式除了回傳判定結果，也會直接對 field 做消耗／生成（唯一入口，狀態變更與判定一起發生）。
    public static class ElementReactionLogic
    {
        // 蒸氣不分陣營：一律記中立代碼。數值對齊 Contracts.Faction.Neutral（＝2），但本檔不得
        // using 該型別（它定義在含 using UnityEngine 的 ICombatTarget.cs），所以直接寫字面值。
        public const int NeutralFactionId = 2;

        // 唯一入口。同一發只會回傳一個反應。
        // 優先序（定死）：流沙 ＞ 水域 ＞ 空地；風只看燃燒區；岩只看水域。
        public static ReactionOutcome Resolve(ElementCast cast, int castFactionId, float x, float z,
                                              float dirX, float dirZ, ElementZoneField field, ElementTuning tuning)
        {
            switch (cast)
            {
                case ElementCast.Rock: return ResolveRock(castFactionId, x, z, field, tuning);
                case ElementCast.Fire: return ResolveFire(castFactionId, x, z, field, tuning);
                case ElementCast.Wind: return ResolveWind(castFactionId, x, z, dirX, dirZ, field, tuning);
                default: return None();
            }
        }

        private static ReactionOutcome None()
        {
            ReactionOutcome outcome = default;
            outcome.Reaction = ElementReaction.None;
            outcome.ConsumedZoneId = -1;
            outcome.NewZoneKindCode = 0;
            outcome.DamageFactionId = -1;
            return outcome;
        }

        // 岩只看水域：落在既有水域內 → 流沙。反應區圓心＝被消耗那個水域的圓心（§4-2），
        // 流沙陣營＝岩（石牆）自己的陣營，不是水域的陣營（★更正★）。
        private static ReactionOutcome ResolveRock(int castFactionId, float x, float z, ElementZoneField field, ElementTuning tuning)
        {
            int waterId = field.FindNearestContaining(x, z, ElementZoneKind.Water);
            if (waterId < 0) return None();

            field.TryGetById(waterId, out ElementZone water);
            field.Terminate(waterId);

            // §4-2：流沙陣營＝石牆（岩）自己的陣營，不是水域的陣營
            int newQuicksandFactionId = castFactionId;
            field.Spawn(ElementZoneKind.Quicksand, water.X, water.Z, tuning.ReactionRadius, tuning.QuicksandDurationSeconds, newQuicksandFactionId);

            ReactionOutcome outcome = default;
            outcome.Reaction = ElementReaction.Quicksand;
            outcome.ConsumedZoneId = waterId;
            outcome.NewZoneKindCode = (int)ElementZoneKind.Quicksand;
            outcome.NewZoneRadius = tuning.ReactionRadius;
            outcome.NewZoneDurationSeconds = tuning.QuicksandDurationSeconds;
            outcome.NewZoneFactionId = newQuicksandFactionId;
            outcome.AoeDamage = 0f;
            outcome.DamageFactionId = -1;
            outcome.IsCombo = true;
            return outcome;
        }

        // 火：流沙 ＞ 水域 ＞ 空地（含既有燃燒區刷新）。優先序定死，只觸發一個。
        private static ReactionOutcome ResolveFire(int castFactionId, float x, float z, ElementZoneField field, ElementTuning tuning)
        {
            int quicksandId = field.FindNearestContaining(x, z, ElementZoneKind.Quicksand);
            if (quicksandId >= 0) return ResolveFireOnQuicksand(castFactionId, quicksandId, field, tuning);

            int waterId = field.FindNearestContaining(x, z, ElementZoneKind.Water);
            if (waterId >= 0) return ResolveFireOnWater(castFactionId, waterId, field, tuning);

            return ResolveFireOnGround(castFactionId, x, z, field, tuning);
        }

        // 陣營方向（★更正★）：火陣營 ≠ 流沙陣營（被困者的友軍放火）→ 救援，解控、不造成傷害。
        // 火陣營 ＝ 流沙陣營（被困者的敵人放火）→ 爆沸，AOE 80、流沙終止。
        private static ReactionOutcome ResolveFireOnQuicksand(int castFactionId, int quicksandId, ElementZoneField field, ElementTuning tuning)
        {
            field.TryGetById(quicksandId, out ElementZone quicksand);
            field.Terminate(quicksandId);

            ReactionOutcome outcome = default;
            outcome.ConsumedZoneId = quicksandId;
            outcome.NewZoneKindCode = 0;
            outcome.DamageFactionId = castFactionId;

            bool isRescue = castFactionId != quicksand.FactionId;
            if (isRescue)
            {
                outcome.Reaction = ElementReaction.Rescue;
                outcome.AoeDamage = 0f;
            }
            else
            {
                outcome.Reaction = ElementReaction.Boil;
                outcome.AoeDamage = tuning.BoilDamage;
            }
            outcome.IsCombo = false;
            return outcome;
        }

        // 反應區圓心＝被消耗那個水域的圓心（§4-2）；蒸氣不分陣營，一律記中立代碼。
        private static ReactionOutcome ResolveFireOnWater(int castFactionId, int waterId, ElementZoneField field, ElementTuning tuning)
        {
            field.TryGetById(waterId, out ElementZone water);
            field.Terminate(waterId);
            field.Spawn(ElementZoneKind.Steam, water.X, water.Z, tuning.ReactionRadius, tuning.SteamDurationSeconds, NeutralFactionId);

            ReactionOutcome outcome = default;
            outcome.Reaction = ElementReaction.Steam;
            outcome.ConsumedZoneId = waterId;
            outcome.NewZoneKindCode = (int)ElementZoneKind.Steam;
            outcome.NewZoneRadius = tuning.ReactionRadius;
            outcome.NewZoneDurationSeconds = tuning.SteamDurationSeconds;
            outcome.NewZoneFactionId = NeutralFactionId;
            outcome.AoeDamage = 0f;
            outcome.DamageFactionId = castFactionId;
            outcome.IsCombo = true;
            return outcome;
        }

        // 空地：直傷＋留一個燃燒區。落在既有燃燒區內則不新增第二個，改成終止舊的、在原地重新生成
        // （效果等同刷新回滿壽命，§4-9）。
        private static ReactionOutcome ResolveFireOnGround(int castFactionId, float x, float z, ElementZoneField field, ElementTuning tuning)
        {
            int existingBurningId = field.FindNearestContaining(x, z, ElementZoneKind.Burning);
            float spawnX = x;
            float spawnZ = z;
            if (existingBurningId >= 0)
            {
                field.TryGetById(existingBurningId, out ElementZone existing);
                spawnX = existing.X;
                spawnZ = existing.Z;
                field.Terminate(existingBurningId);
            }
            field.Spawn(ElementZoneKind.Burning, spawnX, spawnZ, tuning.BurnRadius, tuning.BurnDurationSeconds, castFactionId);

            ReactionOutcome outcome = default;
            outcome.Reaction = ElementReaction.PlainFire;
            outcome.ConsumedZoneId = existingBurningId >= 0 ? existingBurningId : -1;
            outcome.NewZoneKindCode = (int)ElementZoneKind.Burning;
            outcome.NewZoneRadius = tuning.BurnRadius;
            outcome.NewZoneDurationSeconds = tuning.BurnDurationSeconds;
            outcome.NewZoneFactionId = castFactionId;
            outcome.AoeDamage = tuning.FireDirectDamage;
            outcome.DamageFactionId = castFactionId;
            outcome.IsCombo = false;
            return outcome;
        }

        // 風只看燃燒區：掃到＝燃燒區圓心落在扇形內（IsInsideSector），不造成任何非燃燒區的效果。
        private static ReactionOutcome ResolveWind(int castFactionId, float x, float z, float dirX, float dirZ,
                                                    ElementZoneField field, ElementTuning tuning)
        {
            int burningId = FindNearestBurningInSector(x, z, dirX, dirZ, field, tuning);
            if (burningId < 0) return None();

            field.Terminate(burningId);

            ReactionOutcome outcome = default;
            outcome.Reaction = ElementReaction.Firestorm;
            outcome.ConsumedZoneId = burningId;
            outcome.NewZoneKindCode = 0;
            outcome.AoeDamage = tuning.FireDirectDamage * tuning.FirestormDamageMultiplier;
            outcome.DamageFactionId = castFactionId;
            outcome.IsCombo = true;
            return outcome;
        }

        private static int FindNearestBurningInSector(float apexX, float apexZ, float dirX, float dirZ,
                                                        ElementZoneField field, ElementTuning tuning)
        {
            int bestId = -1;
            float bestDistSq = 0f;
            for (int slot = 0; slot < field.Capacity; slot++)
            {
                if (!field.TryGetBySlot(slot, out ElementZone zone)) continue;
                if (zone.Kind != ElementZoneKind.Burning) continue;
                if (!ElementGeometry.IsInsideSector(zone.X, zone.Z, apexX, apexZ, dirX, dirZ,
                        tuning.FirestormRangeMeters, tuning.FirestormAngleDegrees)) continue;

                float dx = zone.X - apexX;
                float dz = zone.Z - apexZ;
                float distSq = dx * dx + dz * dz;
                if (bestId != -1 && distSq > bestDistSq) continue;
                if (bestId != -1 && distSq == bestDistSq && zone.Id >= bestId) continue;

                bestId = zone.Id;
                bestDistSq = distSq;
            }
            return bestId;
        }

        // 敵對判定（兩個方向都要測）：不同陣營代碼＝敵對。DestructibleWall／Neutral 由 Unity 端沿用
        // ICombatTarget.CanBeTargetedBy 處理，純邏輯層只管「英雄 vs 區域」這一條。
        public static bool IsHostile(int castFactionId, int targetFactionId)
        {
            return castFactionId != targetFactionId;
        }
    }
}
