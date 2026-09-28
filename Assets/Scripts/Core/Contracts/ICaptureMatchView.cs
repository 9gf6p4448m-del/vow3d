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

        // v0.9.0 劣勢狂怒剩餘秒數（V090_ENCIRCLE_PLAN.md E15／§2.1-2）；> 0 即生效。
        float BlueRageRemaining { get; }
        float RedRageRemaining { get; }

        // v0.10.0 母板塊聖所／圍城衰減／15 分鐘倒數（V0100_SANCTUARY_PLAN.md E18）。
        float MatchRemainingSeconds { get; }       // 倒數剩餘秒數（900 起算，夾在 0）
        bool EndedByTime { get; }                  // 本局是否因時間到而結束
        bool BlueInSanctuary { get; }              // 只在 Active 時可能為 true（E2）
        bool RedInSanctuary { get; }
        int BlueSanctuaryPercent { get; }          // 聖所強度 0～15（圍城衰減，E11）
        int RedSanctuaryPercent { get; }
        int BlueDamageTakenPercent { get; }        // 受傷百分比 85～100；非 Active 一律 100（E3／E5）
        int RedDamageTakenPercent { get; }
        float BlueChannelRequiredSeconds { get; }  // 本次引導門檻（奪回 1.8、其餘 3.5；沒有在引導時 0，E6／E8）
        float RedChannelRequiredSeconds { get; }

        // v0.11.0：只供本機三選一盤讀取；0 表示目前沒有待選階。
        int BluePendingTalentTier { get; }

        CaptureMatchResult Result { get; }
        CaptureMatchResult LastResult { get; }
    }
}
