namespace Vow.Core.Logic
{
    // v0.14.0 崖台射程與谷底淺水（docs/V0140_CANYON_PLAN.md §4.1、§4.4，2026-09-30 凍結）。零 UnityEngine、零配置。
    // terrain == null（Off 模式、平地夾具）一律回傳原值。
    public static class CanyonRules
    {
        private static readonly CanyonTuning Tuning = new CanyonTuning();

        // 攻擊者出手當下所在位置的射程：ClassAt(攻擊者)==Cliff 才加成（斜坡不算、目標在哪一層不影響）。
        public static float AttackRange(float baseRange, float ax, float az, ITerrainQuery terrain)
        {
            if (terrain == null) return baseRange;
            if (terrain.ClassAt(ax, az, 0) != TerrainClass.Cliff) return baseRange;
            return baseRange * (100 + Tuning.CliffRangePercent) / 100f;
        }

        // 水平距離平方 ≤ 射程平方（含邊界）；射程一律以攻擊者位置計算。
        public static bool InAttackRange(float baseRange, float ax, float az, float tx, float tz, ITerrainQuery terrain)
        {
            float range = AttackRange(baseRange, ax, az, terrain);
            float dx = tx - ax;
            float dz = tz - az;
            return dx * dx + dz * dz <= range * range;
        }

        // 水域半徑：基礎＋天賦（潮汐牽引）＋（圓心在谷底時）淺水加成，三者相加。斜坡上不算谷底。
        public static float WaterRadius(float baseRadius, float talentBonus, float x, float z, ITerrainQuery terrain)
        {
            if (terrain == null || terrain.ClassAt(x, z, 0) != TerrainClass.Canyon) return baseRadius + talentBonus;
            return baseRadius + talentBonus + Tuning.CanyonWaterBonusMeters;
        }
    }
}
