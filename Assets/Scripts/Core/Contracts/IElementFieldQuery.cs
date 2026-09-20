using UnityEngine;

namespace Vow.Core
{
    // 元素區域場對外的唯讀查詢面（Phase 2 批 4）。
    //
    // 為什麼是新介面而不是擴充既有契約：`ARCHITECTURE.md` 的 `ICombatTarget`／`ISkillTelegraphService`
    // 是逐字契約、不得改（先例＝批 1 的 `IRuneCastInput`、批 3 的 `IFactionOwned`／`IWorldTapInput`）。
    // 實作住在 `Vow.Combat.ElementField`，`Vow.Core` 不得反向依賴 `Vow.Combat`，所以英雄只認得這個介面。
    public interface IElementFieldQuery
    {
        // 蒸氣遮蔽（GDD 圍欄九）：目標在霧內、攻擊者不在同一團霧內、且目標未因受擊顯影 → 遮蔽。
        bool IsConcealedFrom(Vector3 targetPosition, bool targetRevealed, Vector3 attackerPosition);

        // 這個座標此刻落在哪個「與 factionId 敵對」的流沙裡；不在任何敵對流沙內 ＝ -1。
        // §2 的介面草案只列了 IsConcealedFrom，但 HeroController 住在 Vow.Core、看不到 ElementField，
        // 縛足／減速要每幀問一次「我在誰的流沙裡」，只能經由這個介面取得（見 p2b4-readback-B.md 疑義 2）。
        int FindHostileQuicksandId(Vector3 position, int factionId);
    }
}
