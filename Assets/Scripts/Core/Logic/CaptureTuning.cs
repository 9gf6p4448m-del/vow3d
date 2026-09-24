namespace Vow.Core.Logic
{
    // v0.8.0 七塊板塊佔領迴圈的數值單一事實來源（V080_CAPTURE_PLAN.md §1.2 E6～E17，
    // 2026-09-24 使用者裁定「全部照表」後凍結，不得私下改動；改動一律走 §4）。
    public sealed class CaptureTuning
    {
        // ── E6：光圈 ──
        public float CircleRadius = 2.5f;

        // ── E9：佔點所需的引導秒數 ──
        public float CaptureSeconds = 3.5f;

        // ── E15：計分脈衝 ──
        public float ScoreTickSeconds = 1f;
        public int ScorePerTilePerTick = 2;

        // ── E16：勝負 ──
        public int WinScore = 1000;

        // ── E4／E20：倒地回城 ──
        public float RespawnSeconds = 5f;

        // ── E17：結算停頓 ──
        public float EndPauseSeconds = 3f;

        // ── E9／E10：對手追打與放棄（含遲滯） ──
        public float ChaseStartDistance = 6f;
        public float ChaseGiveUpDistance = 10f;

        // ── E7：基地復活點（塔心往場外 1.5m） ──
        public float BlueHomeRespawnX = 0f;
        public float BlueHomeRespawnZ = -13.625f;
        public float RedHomeRespawnX = 0f;
        public float RedHomeRespawnZ = 13.625f;

        // ── E8：場邊復活點（基地被搶走） ──
        public float BlueEdgeRespawnX = -17f;
        public float BlueEdgeRespawnZ = -17f;
        public float RedEdgeRespawnX = 17f;
        public float RedEdgeRespawnZ = 17f;
    }
}
