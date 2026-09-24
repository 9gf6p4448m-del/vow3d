using Vow.Core.Logic;

namespace Vow.Core
{
    // ARCHITECTURE.md §參-6：板塊佔領唯讀視圖（V080_CAPTURE_PLAN.md §2.1-2）。
    // HUD（Vow.UI）、板塊顯示（Vow.Combat）、輸入路由（Vow.Input）只依賴這個介面，不直接碰 CaptureMatchLogic。
    // 由 Bootstrap 層實作並轉譯陣營代碼（CaptureMatchLogic 的 int 代碼 → 這裡的 Faction）——CaptureMatchLogic
    // 本身是零 UnityEngine 的純邏輯檔，不能直接 using Faction（定義在含 using UnityEngine 的 ICombatTarget.cs）。
    public interface ICaptureMatchView
    {
        CaptureMatchState State { get; }
        int TileCount { get; }
        Faction OwnerOf(int tileIndex);

        int BlueScore { get; }
        int RedScore { get; }

        int BlueChannelingTile { get; }
        float BlueChannelProgress { get; }
        int RedChannelingTile { get; }
        float RedChannelProgress { get; }

        bool BlueKnockedOut { get; }
        float BlueRespawnRemaining { get; }
        bool RedKnockedOut { get; }
        float RedRespawnRemaining { get; }

        CaptureMatchResult Result { get; }
        CaptureMatchResult LastResult { get; }
    }
}
