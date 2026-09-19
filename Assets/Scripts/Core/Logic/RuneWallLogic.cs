namespace Vow.Core.Logic
{
    // 單面符印石牆的壽命、血量與友軍穿透損耗（GDD §參-2）。不依賴 UnityEngine。
    // 石牆的死因只有三種：壽命到、被打碎、被友軍火力穿到崩解——三者都收斂成 IsAlive 轉為 false。
    public sealed class RuneWallLogic
    {
        private readonly RuneTuning _tuning;

        public RuneWallLogic(RuneTuning tuning)
        {
            _tuning = tuning;
        }

        public bool IsAlive { get; private set; }
        public float MaxHealth { get; private set; }
        public float Health { get; private set; }
        public float RemainingLifespan { get; private set; }
        public int PenetrationCount { get; private set; }
        public int MaxPenetrations => _tuning.MaxPenetrations;

        // 批 3 §2：供彈道分派查詢「這面牆還放不放行」。純唯讀，不改 TryPenetrate 的算式。
        // 注意（§4-4④）：在任何射速下都到不了 10 發——壽命成本（10×0.5s＝整條壽命）永遠先歸零，
        // 所以這個旗標在遊戲內恆為 false，不得拿它寫驗收條文。
        public bool IsPenetrationExhausted => PenetrationCount >= _tuning.MaxPenetrations;

        public void Activate()
        {
            MaxHealth = _tuning.WallMaxHealth;
            Health = MaxHealth;
            RemainingLifespan = _tuning.WallLifespanSeconds;
            PenetrationCount = 0;
            IsAlive = true;
        }

        public void Tick(float deltaSeconds)
        {
            if (!IsAlive) return;
            RemainingLifespan -= deltaSeconds;
            if (RemainingLifespan <= LifespanEpsilon) Kill();
        }

        // 浮點累減的殘渣（5 − 4.99 − 0.01 ≈ 2e-7）不得讓石牆多活一幀
        private const float LifespanEpsilon = 1e-4f;

        public void ApplyDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            Health -= amount;
            if (Health <= 0f) Kill();
        }

        // 友軍彈道穿透一次。回傳 false＝這面牆已不再放行（已死，或已達全隊穿透上限）。
        public bool TryPenetrate(out float damageMultiplier)
        {
            damageMultiplier = 0f;
            if (!IsAlive || PenetrationCount >= _tuning.MaxPenetrations) return false;

            PenetrationCount++;
            damageMultiplier = PenetrationCount <= _tuning.UndecayedPenetrations ? 1f : _tuning.DecayedDamageMultiplier;

            RemainingLifespan -= _tuning.PenetrationLifespanCost;
            Health -= MaxHealth * _tuning.PenetrationHealthFraction;
            if (Health <= 0f || RemainingLifespan <= LifespanEpsilon || PenetrationCount >= _tuning.MaxPenetrations) Kill();
            return true;
        }

        public void Kill()
        {
            IsAlive = false;
            Health = 0f;
            RemainingLifespan = 0f;
        }
    }
}
