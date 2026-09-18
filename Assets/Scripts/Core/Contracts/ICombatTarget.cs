using UnityEngine;

namespace Vow.Core
{
    // ARCHITECTURE.md §參-3
    public interface ICombatTarget
    {
        Transform TargetTransform { get; }
        bool IsAlive { get; }
        Faction TargetFaction { get; }

        void ReceiveDamage(float amount, DamageType type, GameObject instigator);
        bool CanBeTargetedBy(Faction attackerFaction);
    }

    public enum Faction
    {
        BlueTeam,
        RedTeam,
        Neutral,
        DestructibleWall // 石牆為獨立陣營，供近戰破牆護盾校驗
    }

    // ARCHITECTURE.md 引用了 DamageType 但未定義成員；Phase 1 只用到 Physical，其餘為 Phase 2 元素技能預留。
    public enum DamageType
    {
        Physical,
        Elemental,
        True
    }

    // 由螢幕射線命中的 Collider 反查戰鬥目標；輸入層只依賴這個抽象，不認識任何具體目標類別。
    public interface ICombatTargetResolver
    {
        bool TryResolve(Collider collider, out ICombatTarget target);
    }
}
