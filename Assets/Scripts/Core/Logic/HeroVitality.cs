namespace Vow.Core.Logic
{
    public sealed class HeroVitality
    {
        private readonly float _maxHealth;

        public HeroVitality(float maxHealth)
        {
            _maxHealth = maxHealth;
            Health = maxHealth;
        }

        public float Health { get; private set; }
        public float MaxHealth => _maxHealth;
        public bool IsAlive => Health > 0f;

        // 回傳本次是否剛好從活著變成倒地。
        public bool TakeDamage(float amount)
        {
            if (amount <= 0f || !IsAlive) return false;
            Health -= amount;
            if (Health > 0f) return false;
            Health = 0f;
            return true;
        }

        public void Restore() { Health = _maxHealth; }
    }
}
