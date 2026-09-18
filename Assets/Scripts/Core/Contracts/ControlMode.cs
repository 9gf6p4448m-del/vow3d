namespace Vow.Core
{
    // 獨立成檔：純邏輯層（InputRoutingManager）也要用它，不能跟著 IPlayerInputService 一起依賴 UnityEngine。
    public enum ControlMode
    {
        ModeA_FullScreenFlick,  // 模式 A：純粹全螢幕微彈
        ModeB_DualZonePip       // 模式 B：左手身位引導微輪盤 + 右手目標點擊
    }
}
