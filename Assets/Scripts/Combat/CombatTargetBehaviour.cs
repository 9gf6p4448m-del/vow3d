using System;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // ICombatTarget 的共用底座：生命值、陣營校驗、受擊事件。外觀反應（閃白、飄字、血條）由各自的元件訂閱事件處理。
    //
    // Phase 2 批 2 另外提供「0.5m 阻擋格點的登記／撤銷」給石牆用（RuneWall 與 TestWallTarget 都繼承本類別）。
    // 刻意做成呼叫端自己開口的 opt-in：木樁（DummyTarget）不進格點（計畫書 §3）。
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

        // ───────────────────── Phase 2 批 2：阻擋格點登記 ─────────────────────
        // 登記時把當下用的七個參數（中心 x/z、法線 x/z、半寬、半厚、外擴量）存起來，撤銷時用同一組，
        // 不從 transform 重算——牆被移動或縮放過的話重算會撤到別的格子上，留下永久擋路的鬼格（計畫書 §4 假設 8）。

        private BlockGrid _navGrid;
        private float _navInflate;
        private bool _navStamped;   // 這組參數目前有沒有蓋在格點上
        private bool _navWanted;    // 邏輯上這面牆現在該不該擋路（物件暫時停用時仍為 true）
        private float _navCenterX, _navCenterZ, _navNormalX, _navNormalZ, _navHalfWidth, _navHalfThickness;
        private Action<CombatTargetBehaviour> _navStamped1Handler; // 蓋上格子之後要通知誰（推出被壓住的英雄）

        // 這面牆目前有沒有真的蓋在格點上（測試用）。
        public bool NavBlockerStamped => _navStamped;

        // 由 Phase1Bootstrap 注入。換格點前先把舊格點上的登記撤乾淨。
        public void SetNavGrid(BlockGrid grid, float inflateRadius)
        {
            SetNavGrid(grid, inflateRadius, null);
        }

        // stampedHandler：任何一格被蓋上（登記、物件重新啟用、換格點）之後呼叫一次。
        public void SetNavGrid(BlockGrid grid, float inflateRadius, Action<CombatTargetBehaviour> stampedHandler)
        {
            if (_navStamped) Stamp(-1);
            _navGrid = grid;
            _navInflate = inflateRadius;
            _navStamped1Handler = stampedHandler;
            if (_navWanted) Stamp(1);
        }

        // 讀回登記當下用的 OBB（Phase1Bootstrap 拿它去呼叫 HeroLocomotion.EjectFromBox，不必自己重算一份）。
        public bool TryGetNavBlockerBox(out float centerX, out float centerZ, out float normalX, out float normalZ,
                                        out float halfWidth, out float halfThickness)
        {
            centerX = _navCenterX;
            centerZ = _navCenterZ;
            normalX = _navNormalX;
            normalZ = _navNormalZ;
            halfWidth = _navHalfWidth;
            halfThickness = _navHalfThickness;
            return _navWanted;
        }

        // 以 BoxCollider 的實際世界尺寸登記：格點擋住的範圍與 HeroLocomotion 的 SphereCast 實際擋住的範圍
        // 必然來自同一組數字，不會因為 tuning 與場景縮放不同步而各擋各的。
        protected void RegisterNavBlocker(BoxCollider box)
        {
            if (box == null) return;

            Transform boxTransform = box.transform;
            Vector3 center = boxTransform.TransformPoint(box.center);
            Vector3 scale = boxTransform.lossyScale;
            Vector3 forward = boxTransform.forward; // 立方體的 +Z＝牆面法線＝厚度軸

            if (_navStamped) Stamp(-1);
            _navCenterX = center.x;
            _navCenterZ = center.z;
            _navNormalX = forward.x;
            _navNormalZ = forward.z;
            _navHalfWidth = Mathf.Abs(box.size.x * scale.x) * 0.5f;
            _navHalfThickness = Mathf.Abs(box.size.z * scale.z) * 0.5f;
            _navWanted = true;
            Stamp(1);
        }

        // 牆不再存活：壽命到期、被打爆、穿透耗盡、名冊擠掉、Initialize 重入——全部收斂到這裡。
        protected void UnregisterNavBlocker()
        {
            if (_navStamped) Stamp(-1);
            _navWanted = false;
        }

        // 物件被停用／銷毀時把格子還回去，重新啟用時再蓋回來（牆本人還活著的話）。
        protected virtual void OnDisable()
        {
            if (_navStamped) Stamp(-1);
        }

        protected virtual void OnEnable()
        {
            if (_navWanted && !_navStamped) Stamp(1);
        }

        // r1 對抗審查 C1（§6 R4）：推出要按「危險的效果」寫，不按「已知的入口」寫。
        // 危險的效果是「英雄與一面正在擋路的牆重疊」，能造成它的入口有三個——RegisterNavBlocker（符印牆立起、
        // 測試牆重生）、OnEnable（物件重新啟用）、SetNavGrid（換格點）——它們全部收斂在這一個 Stamp(+1) 上，
        // 所以通知掛在這裡，不掛在 RuneWall.OnActivated 那唯一一個入口（修復前只補了實務上踩不到的那條）。
        private void Stamp(int delta)
        {
            if (_navGrid == null) return;
            _navGrid.StampBox(_navCenterX, _navCenterZ, _navNormalX, _navNormalZ,
                              _navHalfWidth, _navHalfThickness, _navInflate, delta);
            _navStamped = delta > 0;
            if (_navStamped && _navStamped1Handler != null) _navStamped1Handler(this);
        }
    }
}
