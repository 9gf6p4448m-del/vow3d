namespace Vow.Core.Logic
{
    // 正式 19 板塊佔領局的雙方視野；歸屬永遠由 match 即時讀取，不保留揭露快取。
    public static class CaptureVisibilityLogic
    {
        public const float LocalVisionRadius = 6f;
        private const float LocalVisionRadiusSquared = LocalVisionRadius * LocalVisionRadius;

        public static bool AppliesTo(CaptureMatchLogic match)
        {
            return match != null && match.FogEnabled && match.State == CaptureMatchState.Active
                && ReferenceEquals(match.Spec, CaptureBoardSpec.V0100Sanctuary);
        }

        public static bool HasTrueVision(CaptureMatchLogic match, int viewerSide, float targetX, float targetZ)
        {
            if (!AppliesTo(match)) return false;
            int tile = match.Spec.TileAt(targetX, targetZ);
            return tile >= 0 && match.OwnerOf(tile) == viewerSide;
        }

        public static bool CanSee(
            CaptureMatchLogic match, int viewerSide,
            float viewerX, float viewerZ, bool viewerKnockedOut,
            float targetX, float targetZ)
        {
            if (!AppliesTo(match)) return true;
            if (HasTrueVision(match, viewerSide, targetX, targetZ)) return true;
            if (viewerKnockedOut) return false;
            float dx = targetX - viewerX;
            float dz = targetZ - viewerZ;
            return dx * dx + dz * dz <= LocalVisionRadiusSquared;
        }
    }
}
