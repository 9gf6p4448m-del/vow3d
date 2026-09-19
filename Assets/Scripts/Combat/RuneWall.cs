using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 符印石牆（GDD §參-2）：內部用 RuneWallLogic 管壽命／血量／友軍彈道穿透，外殼沿用 CombatTargetBehaviour
    // 既有的受擊／死亡流程。三條死路——壽命到、被打碎、被友軍彈道打穿——全部收斂到同一個 HandleDeath()。
    // 紅線 5：只准 BoxCollider，嚴禁 NavMeshObstacle／carving；阻擋完全靠物理碰撞（見 HeroLocomotion.ApplyDisplacement）。
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RuneWall : CombatTargetBehaviour, IRuneWall
    {
        private BoxCollider _collider;
        private Renderer _renderer;
        private RuneTuning _tuning;
        private RuneWallLogic _logic;
        private RuneCaster _caster;
        private int _slotIndex = -1;

        public Faction OwnerFaction { get; private set; }
        public float RemainingLifespan => _logic != null ? _logic.RemainingLifespan : 0f;
        public int MaxPenetrationCount => _logic != null ? _logic.MaxPenetrations : 0;
        public int CurrentPenetrationCount => _logic != null ? _logic.PenetrationCount : 0;

        protected override void Awake()
        {
            base.Awake();
            _collider = GetComponent<BoxCollider>();
            _renderer = GetComponentInChildren<Renderer>();
            if (_tuning == null) _tuning = new RuneTuning();
            if (_logic == null) _logic = new RuneWallLogic(_tuning);

            // 池中待命：GameObject 保持啟用，只關 Collider／Renderer；IsAlive 必須是 false（不可被鎖定攻擊），
            // 這樣 Phase1Bootstrap 的 FindObjectsOfType<CombatTargetBehaviour> 才掃得到並註冊，但打不到它。
            if (IsAlive) ReceiveDamage(Health, DamageType.True, null);
            else
            {
                _collider.enabled = false;
                if (_renderer != null) _renderer.enabled = false;
            }
        }

        // 由 RuneCaster 對池中每一面牆呼叫一次，注入手感數值的單一事實來源（HeroTuningAsset.Rune）。
        // r1 對抗審查 H5：若這面牆當下還活著，先收掉再換 _logic——否則舊的存活狀態失去追蹤，變成一面
        // 不會到期、Collider 還開著、名額也回不去名冊的永久牆（RuneCaster 重新 Initialize 時會摸到這條路徑）。
        public void Initialize(RuneTuning tuning)
        {
            if (IsAlive) ForceKill();
            _tuning = tuning;
            _logic = new RuneWallLogic(tuning);
        }

        // 由 RuneCaster 施放時呼叫：立牆於指定位置與朝向。caster／slotIndex 供壽命到期時自己通知名冊釋放名額。
        public void Activate(Vector3 position, Quaternion rotation, Faction owner, RuneCaster caster, int slotIndex)
        {
            _caster = caster;
            _slotIndex = slotIndex;
            OwnerFaction = owner;

            transform.SetPositionAndRotation(position, rotation);
            Configure(_tuning.WallMaxHealth, Faction.DestructibleWall);
            _logic.Activate();

            _collider.enabled = true;
            if (_renderer != null) _renderer.enabled = true;

            // 登記格點：推出被壓住的英雄由 CombatTargetBehaviour.Stamp 統一通知（§6 R4），
            // 這裡不再另開一條只有符印牆走得到的事件。
            RegisterNavBlocker(_collider);
        }

        private void Update()
        {
            if (!_logic.IsAlive) return;
            _logic.Tick(Time.deltaTime);
            if (!_logic.IsAlive) ForceKill(); // 壽命到：與被打碎／被穿透打死走同一條收斂路徑
        }

        // 五條離場路徑（壽命到期、被打爆、穿透耗盡、名冊擠掉、Initialize 重入）在 RuneWall 內部全部收斂到
        // ForceKill → ReceiveDamage → 這裡，所以撤銷格點只需要寫在這一個地方；
        // 第六條「物件停用／銷毀」由 CombatTargetBehaviour.OnDisable 負責。
        protected override void HandleDeath()
        {
            _logic.Kill();
            _collider.enabled = false;
            if (_renderer != null) _renderer.enabled = false;
            UnregisterNavBlocker();
            _caster?.ReleaseSlot(_slotIndex);
        }

        // 坍塌回調（IRuneWall）：名冊超額時最舊的一面、或未來近戰／技能強制拆牆，都走這裡。
        public void CollapseWall(bool crushedByMelee)
        {
            ForceKill();
        }

        // 友軍彈道穿透：批 3 才有子彈真的呼叫，本批只需要介面能編譯並符合已測過的數學（RuneWallLogicTests）。
        public bool TryPenetrateBullet(Vector3 bulletVelocity, out float damageMultiplier)
        {
            bool penetrated = _logic.TryPenetrate(out damageMultiplier);
            if (!_logic.IsAlive && IsAlive) ForceKill(); // 穿透打死了：同步基底血量，走同一條 HandleDeath
            return penetrated;
        }

        // 統一的「立刻死亡」入口：把 CombatTargetBehaviour 的既有健康值歸零，藉由它既有的
        // ReceiveDamage → HandleDeath 流程收斂，避免另開一條不會呼叫 HandleDeath 的死亡路徑。
        private void ForceKill()
        {
            if (!IsAlive)
            {
                _logic.Kill();
                return;
            }
            ReceiveDamage(Health, DamageType.True, null);
        }
    }
}
