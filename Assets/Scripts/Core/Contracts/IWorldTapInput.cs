namespace Vow.Core
{
    // 世界點擊的注入面。批 3 把「點自家石牆＝點到牆後地板」從圖層手段換成陣營校驗之後，
    // 點擊路徑需要知道「本地玩家是哪一隊」，而驗收（V4-e／V4-f）必須走真實的 OnWorldTap，
    // 不能只驗 Physics.Raycast 的結果。
    //
    // 為什麼是新介面而不是擴充 IPlayerInputService：後者是 ARCHITECTURE.md 的逐字契約、不得改
    // （先例＝批 1 的 IRuneCastInput）。PlayMode 測試 asmdef 只看得到 Vow.Core，
    // 透過這個介面由 Phase1Bootstrap 交出實作，就不必為了測試加 asmdef 引用。
    public interface IWorldTapInput
    {
        Faction LocalFaction { get; }
        void SetLocalFaction(Faction faction);

        // 螢幕座標（原點左下、像素）。命中解析與「跳過己方石牆」的規則由實作負責。
        void OnWorldTap(float screenX, float screenY);

        // 送出一次完整的觸控點擊（按下＋放開，同一點），走的是與真實手指一模一樣的分流路徑：
        // 邊緣死區 → UI 區域 → 符印 → 微輪盤 → 世界。驗收 HUD 按鈕要驗「區域判定真的把手指攔下來」，
        // 而不是直接呼叫按鈕的處理常式（r1 對抗審查 MEDIUM-2）。
        void SendScreenTap(float screenX, float screenY);
    }
}
