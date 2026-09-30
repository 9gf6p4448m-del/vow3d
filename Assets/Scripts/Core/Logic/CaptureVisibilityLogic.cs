namespace Vow.Core.Logic
{
    // 正式 19 板塊佔領局的雙方視野；歸屬永遠由 match 即時讀取，不保留揭露快取。
    // v0.14.0（V0140_CANYON_PLAN.md §4.2、§4.3、§4.7，2026-09-30 凍結）：迷霧改讀規格旗標 Spec.FogEnabled；
    // 地形規則（崖台 8m、谷底看不到崖台）由 match.Spec.Terrain 決定；開火顯形由呼叫端持有的 RevealTracker 決定。
    // 所有下游（顯隱、板塊霧、點選、AI）只呼叫 CanSee／HasTrueVision，不在下游另寫地形判斷。
    public static class CaptureVisibilityLogic
    {
        public const float LocalVisionRadius = 6f;
        private const float LocalVisionRadiusSquared = LocalVisionRadius * LocalVisionRadius;
        private static readonly float CliffVisionRadiusSquared = CliffVisionRadius() * CliffVisionRadius();

        public static bool AppliesTo(CaptureMatchLogic match)
        {
            return match != null && match.FogEnabled && match.State == CaptureMatchState.Active
                && match.Spec.FogEnabled;
        }

        public static bool HasTrueVision(CaptureMatchLogic match, int viewerSide, float targetX, float targetZ)
        {
            if (!AppliesTo(match)) return false;
            int tile = match.Spec.TileAt(targetX, targetZ);
            return tile >= 0 && match.OwnerOf(tile) == viewerSide;
        }

        // 既有多載：等同 tracker == null 的新多載（地形規則照樣由 match.Spec.Terrain 決定）。
        public static bool CanSee(
            CaptureMatchLogic match, int viewerSide,
            float viewerX, float viewerZ, bool viewerKnockedOut,
            float targetX, float targetZ)
        {
            return CanSee(match, null, RevealUnit.BlueHero, viewerSide, viewerX, viewerZ, viewerKnockedOut, targetX, targetZ);
        }

        // §4.3 合成順序：1 不適用迷霧 → 看得到；1.5 目標對觀看方顯形 → 看得到；2 真視野 → 看得到；
        // 3 觀看者倒地 → 看不到；4 觀看者在谷底且目標在崖台 → 看不到；5 距離 ≤ 半徑（觀看者在崖台 8、其他 6）。
        public static bool CanSee(
            CaptureMatchLogic match, RevealTracker tracker, RevealUnit targetUnit, int viewerSide,
            float viewerX, float viewerZ, bool viewerKnockedOut,
            float targetX, float targetZ)
        {
            if (!AppliesTo(match)) return true;
            ITerrainQuery terrain = match.Spec.Terrain;
            if (tracker != null && tracker.IsRevealedTo(targetUnit, viewerSide)) return true;
            if (HasTrueVision(match, viewerSide, targetX, targetZ)) return true;
            if (viewerKnockedOut) return false;
            if (CanyonHidesCliff(terrain, viewerX, viewerZ, targetX, targetZ)) return false;
            float dx = targetX - viewerX;
            float dz = targetZ - viewerZ;
            return dx * dx + dz * dz <= VisionRadiusSquared(terrain, viewerX, viewerZ);
        }

        // 只限制「谷底看崖台」這一個方向；斜坡上的觀看者與目標都不算谷底／崖台。
        private static bool CanyonHidesCliff(ITerrainQuery terrain, float viewerX, float viewerZ, float targetX, float targetZ)
        {
            if (terrain == null) return false;
            return terrain.ClassAt(viewerX, viewerZ, 0) == TerrainClass.Canyon
                && terrain.ClassAt(targetX, targetZ, 0) == TerrainClass.Cliff;
        }

        private static float VisionRadiusSquared(ITerrainQuery terrain, float viewerX, float viewerZ)
        {
            if (terrain != null && terrain.ClassAt(viewerX, viewerZ, 0) == TerrainClass.Cliff) return CliffVisionRadiusSquared;
            return LocalVisionRadiusSquared;
        }

        private static float CliffVisionRadius() => new CanyonTuning().CliffVisionRadius;
    }
}
