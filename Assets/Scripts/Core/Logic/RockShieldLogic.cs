namespace Vow.Core.Logic
{
    // 破牆護盾（GDD §參-2）：近戰砸碎敵方／中立石牆 → 取得 150 點、2.5s 的護盾。
    // 不依賴 UnityEngine；陣營用 int 代碼（Core/Logic 不得引用 Contracts 的 Faction）。
    public sealed class RockShieldLogic
    {
        // 浮點累減的殘渣（2.5 − 2.49 − 0.01 ≈ 2e-7）不得讓護盾多活一幀（與 RuneWallLogic 同一套處理）。
        private const float TimerEpsilon = 1e-4f;

        private readonly ProjectileTuning _tuning;
        private float _amount;
        private float _remainingSeconds;

        public RockShieldLogic(ProjectileTuning tuning)
        {
            _tuning = tuning;
        }

        public float Amount => _amount;
        public float RemainingSeconds => _remainingSeconds;
        public bool IsActive => _amount > 0f && _remainingSeconds > 0f;

        // 重複取得＝刷新回滿值，不疊加（使用者裁定 3）。
        public void Grant()
        {
            _amount = _tuning.ShieldAmount;
            _remainingSeconds = _tuning.ShieldDurationSeconds;
        }

        public void Tick(float deltaSeconds)
        {
            if (_remainingSeconds <= 0f) return;

            _remainingSeconds -= deltaSeconds;
            if (_remainingSeconds > TimerEpsilon) return;

            _remainingSeconds = 0f;
            _amount = 0f;
        }

        // 回傳穿過護盾的殘餘傷害。護盾值歸零不代表倒數結束（倒數另外走 Tick）。
        public float Absorb(float incomingDamage)
        {
            if (incomingDamage <= 0f) return 0f;
            if (!IsActive) return incomingDamage;

            float absorbed = incomingDamage < _amount ? incomingDamage : _amount;
            _amount -= absorbed;
            return incomingDamage - absorbed;
        }

        // 授予規則（§4-3）。四個條件缺一不給；ownerKnown=false 一律不給＝fail-closed。
        // 遠程（子彈）打碎牆不會走到這裡——護盾是近戰特權（GDD.md:95）。
        public static bool ShouldGrantOnMeleeKill(bool targetKilled, bool targetIsWall,
                                                  bool ownerKnown, int ownerFactionId, int attackerFactionId)
        {
            if (!targetKilled) return false;
            if (!targetIsWall) return false;
            if (!ownerKnown) return false;
            return ownerFactionId != attackerFactionId;
        }
    }
}
