namespace Vow.Core
{
    // 除錯 HUD 的可驗收面。批 3 的兩顆鈕（ENEMY WALL／TURRET）與護盾列要能被 PlayMode 測試
    // 按下與讀取，但 HUD 實作住在 Vow.UI，而 PlayMode 測試 asmdef 只引用 Vow.Core／Vow.Combat／
    // Vow.Bootstrap（V6 明文不得為了測試加引用）——所以把「按鈕按下去」與「面板上顯示什麼字」
    // 收斂成這個介面，由 Phase1Bootstrap 交出實作。
    //
    // 真實觸控 → UI 區域分流那一段由 InputRoutingManager 的 EditMode 測試負責；
    // 這裡按的是「區域被點到之後」執行的同一個方法。
    public interface IDebugHudPanel
    {
        string TurretButtonLabel { get; }
        string EnemyWallButtonLabel { get; }
        string ShieldValueLabel { get; }

        void PressTurretButton();
        void PressEnemyWallButton();
    }
}
