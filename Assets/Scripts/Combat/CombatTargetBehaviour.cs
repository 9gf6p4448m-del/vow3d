using System;
using UnityEngine;
using Vow.Core;

namespace Vow.Combat
{
    // ICombatTarget 的共用底座：生命值、陣營校驗、受擊事件。外觀反應（閃白、飄字、血條）由各自的元件訂閱事件處理。
    public abstract class CombatTargetBehaviour : MonoBehaviour, ICombatTarget
    {
        [SerializeField] private float _maxHealth = 600f;
        [SerializeField] private Faction _faction = Faction.RedTeam;

        private Transform _cachedTransform;
        private Collider[] _colliders;
        private float _health;

        public Transform TargetTransform => _cachedTransform;
        public bool IsAlive => _health > 0f;
        public Faction TargetFaction => _faction;
        public float MaxHealth => _maxHealth;
        public float Health => _health;
        public float HealthNormalized => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;

        public Collider[] TargetColliders
        {
            get
            {
                if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
                return _colliders;
            }
        }

        public event Action<float> OnDamaged;   // 實際扣除的傷害量
        public event Action OnDied;
        public event Action OnRevived;

        protected virtual void Awake()
        {
            _cachedTransform = transform;
            _health = _maxHealth;
        }

        public void Configure(float maxHealth, Faction faction)
        {
            _maxHealth = maxHealth;
            _faction = faction;
            _health = maxHealth;
        }

        // 石牆（DestructibleWall）與中立目標任何陣營都可以打；其餘不得攻擊同陣營。
        public virtual bool CanBeTargetedBy(Faction attackerFaction)
        {
            if (_faction == Faction.DestructibleWall || _faction == Faction.Neutral) return true;
            return attackerFaction != _faction;
        }

        public void ReceiveDamage(float amount, DamageType type, GameObject instigator)
        {
            if (!IsAlive || amount <= 0f) return;

            float applied = Mathf.Min(amount, _health);
            _health -= applied;
            OnDamaged?.Invoke(applied);

            if (_health <= 0f)
            {
                _health = 0f;
                OnDied?.Invoke();
                HandleDeath();
            }
        }

        protected void Revive()
        {
            _health = _maxHealth;
            OnRevived?.Invoke();
        }

        protected abstract void HandleDeath();
    }
}
