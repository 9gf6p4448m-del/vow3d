namespace Vow.Core
{
    // 中立世界目標可宣告跨迷霧揭露；Core 與輸入層只認介面。
    public interface IGlobalObjectiveVisibility
    {
        bool IsGloballyVisibleTo(Faction viewer);
    }
}
