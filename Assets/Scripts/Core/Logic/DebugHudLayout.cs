namespace Vow.Core.Logic
{
    // 除錯 HUD 左上面板的版面計算（PHASE2_BATCH4_PLAN.md §5 V4-o／V4-p）。
    //
    // 為什麼搬出 DebugHud：`RecalculateLayout` 直接讀 `Screen.width/height/dpi`，batchmode 改不了，
    // 「四顆新鈕不與既有鈕／符印鈕重疊、面板不長出畫面」就沒有可機械判定的入口。
    // 計畫原文寫的是「Vow.UI 的 internal 入口」，但 V6-b 禁止改 asmdef，而兩個測試 asmdef 都沒有引用
    // Vow.UI（EditMode＝Vow.Core＋Vow.Input、PlayMode＝Vow.Core／Vow.Combat／Vow.Bootstrap），
    // 所以改放在 Core/Logic（零 UnityEngine，順便進得了純邏輯專案）。見 p2b4-readback-B.md 疑義 1。
    //
    // 既有六個矩形的算式**逐字**搬自 v0.5.0 的 DebugHud.RecalculateLayout（`:233-250`），一個運算元都沒動。
    public struct HudRect
    {
        public float X, Y, Width, Height;

        public HudRect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public float XMin => X;
        public float YMin => Y;
        public float XMax => X + Width;
        public float YMax => Y + Height;

        // 邊界相接（一個的 XMax 等於另一個的 XMin）不算相交，與 UnityEngine.Rect.Overlaps 的語意相同。
        public bool Overlaps(HudRect other)
        {
            return XMin < other.XMax && other.XMin < XMax
                   && YMin < other.YMax && other.YMin < YMax;
        }
    }

    public struct DebugHudLayout
    {
        public const float ReferenceDpi = 160f;
        public const float Pad = 8f;
        public const float Row = 22f;
        public const float PanelWidth = 250f;
        public const float InfoRows = 10f; // 批 3 多一列 SHIELD；批 4 不動它

        // IMGUI 座標（原點左上、未乘 Scale）
        public HudRect Mode, Hitbox, Latency, Grid, EnemyWall, Turret;
        public HudRect Water, Fire, Wind, Elem;
        public HudRect MatchPanel, Capture; // v0.8.0 佔領對局面板與 CAPTURE 鈕（V080_CAPTURE_PLAN.md §2.4）
        public float Scale;
        public float PanelHeight;

        // captureModeActive 為選用參數（預設 false）：既有呼叫端不必改，MatchPanel/Capture 一律照算
        // （不影響左側面板與既有六個矩形，V-A24／既有 V4-o／V4-p 不受影響）。
        public static DebugHudLayout Compute(float screenWidth, float screenHeight, float dpi,
                                             bool hasLatencyRow, bool hasGridRow, bool hasWallOrTurretRow,
                                             bool captureModeActive = false)
        {
            DebugHudLayout layout = default;
            layout.Scale = dpi > 0f ? (dpi / ReferenceDpi > 1f ? dpi / ReferenceDpi : 1f) : 1f;

            float y = Pad + Row * InfoRows + Pad;
            float buttonWidth = (PanelWidth - Pad * 3f) * 0.5f;
            layout.Mode = new HudRect(Pad * 2f, y, buttonWidth, Row * 1.6f);
            layout.Hitbox = new HudRect(Pad * 3f + buttonWidth, y, buttonWidth, Row * 1.6f);
            y += Row * 1.6f + Pad;
            layout.Latency = new HudRect(Pad * 2f, y, PanelWidth - Pad * 2f, Row * 1.6f);
            if (hasLatencyRow) y += Row * 1.6f + Pad;
            layout.Grid = new HudRect(Pad * 2f, y, PanelWidth - Pad * 2f, Row * 1.6f);
            if (hasGridRow) y += Row * 1.6f + Pad;
            layout.EnemyWall = new HudRect(Pad * 2f, y, buttonWidth, Row * 1.6f);
            layout.Turret = new HudRect(Pad * 3f + buttonWidth, y, buttonWidth, Row * 1.6f);

            // ── 批 4：接在 _turretRect 那一列之後的兩列（§2「HUD 版面」）──
            y += Row * 1.6f + Pad;
            float thirdWidth = (PanelWidth - Pad * 4f) / 3f;
            layout.Water = new HudRect(Pad * 2f, y, thirdWidth, Row * 1.6f);
            layout.Fire = new HudRect(Pad * 3f + thirdWidth, y, thirdWidth, Row * 1.6f);
            layout.Wind = new HudRect(Pad * 4f + thirdWidth * 2f, y, thirdWidth, Row * 1.6f);
            y += Row * 1.6f + Pad;
            layout.Elem = new HudRect(Pad * 2f, y, PanelWidth - Pad * 2f, Row * 1.6f);

            // 面板底緣：算式與 OnGUI 的 panelHeight 同源（批 4 的兩列一律存在，與四顆鈕的委派是否接上無關——
            // 不接上時 DebugHud 根本不畫也不登記，版面本身仍照算，測試才有固定的輸入）。
            layout.PanelHeight = Pad + Row * InfoRows + Pad + Row * 1.6f + Pad
                                 + (hasLatencyRow ? Row * 1.6f + Pad : 0f)
                                 + (hasGridRow ? Row * 1.6f + Pad : 0f)
                                 + (hasWallOrTurretRow ? Row * 1.6f + Pad : 0f)
                                 + (Row * 1.6f + Pad) * 2f;

            // v0.8.0：右上對局面板＋CAPTURE 鈕（§2.4）。與左側面板的算式完全獨立，不讀不寫上面任何欄位。
            float matchPanelX = screenWidth / layout.Scale - 176f - Pad;
            if (matchPanelX < PanelWidth + Pad * 2f) matchPanelX = PanelWidth + Pad * 2f;
            float matchPanelHeight = captureModeActive ? 116f : 72f;
            layout.MatchPanel = new HudRect(matchPanelX, Pad, 176f, matchPanelHeight);
            layout.Capture = new HudRect(matchPanelX, Pad + 116f + Pad, 176f, Row * 1.6f);

            return layout;
        }
    }
}
