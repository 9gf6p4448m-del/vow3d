namespace Vow.Core
{
    // 「這個目標屬於誰」。石牆為獨立陣營（Faction.DestructibleWall），所以 ICombatTarget.TargetFaction
    // 答不出「這是誰的牆」——近戰破牆護盾的陣營校驗（GDD §參-2）與友軍彈道穿透都需要這一條。
    //
    // ARCHITECTURE.md §參-3 的 ICombatTarget 是逐字契約、不得改；要補東西一律新增介面
    // （先例＝批 1 的 Core/Contracts/IRuneCastInput）。
    public interface IFactionOwned
    {
        Faction OwnerFaction { get; }
    }
}
