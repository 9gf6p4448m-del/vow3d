using UnityEngine;

namespace Vow.Core
{
    // ARCHITECTURE.md §參-4。Phase 1 冷庫協議：僅保留契約，不實作（測試石牆走 ICombatTarget）。
    public interface IRuneWall : ICombatTarget
    {
        float RemainingLifespan { get; }
        int MaxPenetrationCount { get; } // 10 發
        int CurrentPenetrationCount { get; }

        // 友軍彈道穿透：前 5 發無衰減，每發扣 10% 耐久與 0.5s 壽命
        bool TryPenetrateBullet(Vector3 bulletVelocity, out float damageMultiplier);

        // 坍塌回調 (杜絕全圖 NavMesh 烘焙)
        void CollapseWall(bool crushedByMelee);
    }
}
