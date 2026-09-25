using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Bootstrap
{
    // ICaptureMatchView 的唯一實作（V080_CAPTURE_PLAN.md §2.1-2）：把 CaptureMatchLogic 的 int 陣營代碼轉成 Faction。
    // 轉接放在 Bootstrap 層——CaptureMatchLogic 是零 UnityEngine 的純邏輯檔，不能引用定義在含 UnityEngine 檔案裡的 Faction。
    // 代碼與 Faction 的序數本來就對齊（Blue=0、Red=1、Neutral=2），這裡仍逐一對應，不靠強制轉型賭序數不變。
    internal sealed class CaptureMatchView : ICaptureMatchView
    {
        private readonly CaptureMatchLogic _logic;

        public CaptureMatchView(CaptureMatchLogic logic) { _logic = logic; }

        public CaptureMatchState State => _logic.State;
        public int TileCount => _logic.TileCount;

        // 第一次開局前 CaptureMatchLogic 的歸屬陣列還是 int 預設值 0（＝Blue 代碼），不是「開局狀態」；
        // 這段期間（佔領待機、從未開過局）一律回中立，板塊才不會整片顯示成藍色。開過局之後忠實轉達。
        public Faction OwnerOf(int tileIndex)
        {
            if (_logic.StartCount == 0) return Faction.Neutral;
            int owner = _logic.OwnerOf(tileIndex);
            if (owner == CaptureMatchLogic.BlueFactionId) return Faction.BlueTeam;
            if (owner == CaptureMatchLogic.RedFactionId) return Faction.RedTeam;
            return Faction.Neutral;
        }

        public int BlueScore => _logic.BlueScore;
        public int RedScore => _logic.RedScore;

        public int BlueChannelingTile => _logic.BlueChannelingTile;
        public float BlueChannelProgress => _logic.BlueChannelProgress;
        public int RedChannelingTile => _logic.RedChannelingTile;
        public float RedChannelProgress => _logic.RedChannelProgress;

        public bool BlueKnockedOut => _logic.BlueKnockedOut;
        public float BlueRespawnRemaining => _logic.BlueRespawnRemaining;
        public bool RedKnockedOut => _logic.RedKnockedOut;
        public float RedRespawnRemaining => _logic.RedRespawnRemaining;

        public float BlueRageRemaining => _logic.BlueRageRemaining;
        public float RedRageRemaining => _logic.RedRageRemaining;

        public CaptureMatchResult Result => _logic.Result;
        public CaptureMatchResult LastResult => _logic.LastResult;
    }
}
