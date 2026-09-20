namespace Vow.Core
{
    // 「這個目標此刻是不是因為受擊而顯影」（GDD 圍欄九：霧內目標受傷後顯影 1.5s，期間可被鎖定）。
    //
    // 不改 `ICombatTarget.CanBeTargetedBy(Faction)` 的簽章（`ARCHITECTURE.md §參-3` 逐字契約）：
    // 它拿不到攻擊者座標，而蒸氣規則②（同一團霧裡的攻擊者照樣打得到）需要。
    // 所以顯影旗標另開這個介面，遮蔽判定收斂在 `HeroController.CanEngage` 一個地方。
    public interface IConcealable
    {
        bool IsRevealed { get; }
    }
}
