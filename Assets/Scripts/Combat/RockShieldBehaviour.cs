using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 破牆護盾的 Unity 外殼（GDD §參-2）。規則本體在 RockShieldLogic（純邏輯、有測試）。
    //
    // 授予的掛點刻意選 HeroController.OnAttackHitResolved（HeroController.cs:39，於 :227 送出），
    // 不是牆的 OnDied——因為「壽命到期／被友軍子彈穿到崩解／被第 3 面牆擠掉」這三條**不經過英雄的攻擊結算**，
    // 掛在 OnDied 上它們全都會給盾（V4-i 就是守這件事）。HeroController.cs 本批零改動。
    public sealed class RockShieldBehaviour : MonoBehaviour, IRockShield
    {
        private RockShieldLogic _logic;
        private HeroController _hero;
        private int _attackerFactionId;
        private bool _subscribed;

        public float Amount => _logic != null ? _logic.Amount : 0f;
        public float RemainingSeconds => _logic != null ? _logic.RemainingSeconds : 0f;
        public bool IsActive => _logic != null && _logic.IsActive;

        // 活性計數（V5 的量測窗口用）。
        public int GrantCount { get; private set; }

        public void Initialize(HeroController hero, ProjectileTuning tuning)
        {
            Unsubscribe();

            _logic = new RockShieldLogic(tuning);
            _hero = hero;
            GrantCount = 0;
            if (_hero == null) return;

            _attackerFactionId = (int)_hero.HeroFaction;
            _hero.OnAttackHitResolved += HandleAttackHitResolved;
            _subscribed = true;
        }

        public void Grant()
        {
            if (_logic == null) return;
            _logic.Grant();
            GrantCount++;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _hero == null) return;
            _hero.OnAttackHitResolved -= HandleAttackHitResolved;
            _subscribed = false;
        }

        private void HandleAttackHitResolved(ICombatTarget target)
        {
            if (target == null || _logic == null) return;

            IFactionOwned owned = target as IFactionOwned;
            bool ownerKnown = owned != null;
            int ownerFactionId = ownerKnown ? (int)owned.OwnerFaction : -1;

            if (!RockShieldLogic.ShouldGrantOnMeleeKill(!target.IsAlive,
                                                        target.TargetFaction == Faction.DestructibleWall,
                                                        ownerKnown, ownerFactionId, _attackerFactionId))
                return;

            Grant();
        }

        private void Update()
        {
            if (_logic == null) return;
            _logic.Tick(Time.deltaTime);
        }
    }
}
