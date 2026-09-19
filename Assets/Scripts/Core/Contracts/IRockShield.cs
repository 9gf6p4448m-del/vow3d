namespace Vow.Core
{
    // 破牆護盾（GDD §參-2）：近戰砸碎敵方／中立石牆取得的一段式護盾。
    // 規則本體在 Vow.Core.Logic.RockShieldLogic（純邏輯、有測試），這個介面只是讓
    // 除錯 HUD 與未來的傷害結算看得到它，不必認識 Vow.Combat 的具體元件。
    public interface IRockShield
    {
        float Amount { get; }
        float RemainingSeconds { get; }
        void Grant();
    }
}
