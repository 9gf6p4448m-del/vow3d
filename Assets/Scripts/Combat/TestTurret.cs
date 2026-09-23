using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 友軍測試砲台（Phase 2 批 3 的驗收設施，不是遊戲功能）。固定位置、固定朝木樁、藍隊。
    //
    // **預設關閉**（§4-7）：木樁血 600，常開會以 80 DPS 持續把它打死，污染 Phase 1 的手感試玩
    // 與所有斷言木樁血量的既有測試。不按 HUD 的 TURRET 鈕時，場上行為與 v0.4.1 逐值相同。
    // 沒有 Collider（不擋路、不吃點擊、不改任何既有導航與點擊行為）。
    public sealed class TestTurret : MonoBehaviour
    {
        [SerializeField] private Projectile[] _pool;

        private ProjectileTuning _tuning;
        private Transform _transform;
        private Vector3 _direction;
        private bool _hasDirection;
        private float _cooldown;
        private int _nextSlot;

        public bool IsFiring { get; private set; }
        public int ShotsFired { get; private set; }

        // 池裡每一發的累計戰果（V4-n 的「穿透數＋擋下數 == 發射數」讀這裡）。
        public int Penetrations => Sum(0);
        public int Blocks => Sum(1);
        public int TargetHits => Sum(2);

        public void Initialize(ProjectileTuning tuning, ICombatTargetResolver resolver, Faction ownerFaction,
                               Transform aimTarget)
        {
            _tuning = tuning;
            _transform = transform;
            IsFiring = false;
            ShotsFired = 0;
            _cooldown = 0f;
            _hasDirection = false;

            // 開火方向在這裡算一次就固定（木樁不移動）；木樁不在場則不開火。
            if (aimTarget != null)
            {
                Vector3 delta = aimTarget.position - _transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > 1e-6f)
                {
                    _direction = delta.normalized;
                    _hasDirection = true;
                }
            }

            if (_pool == null) return;
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null) _pool[i].Initialize(tuning, resolver, ownerFaction);
        }

        public void SetFiring(bool firing)
        {
            if (IsFiring == firing) return;
            IsFiring = firing;
            _cooldown = 0f; // 打開時下一幀就射第一發；關閉時已在飛的子彈照常結算
        }

        public void CancelProjectilesForRound()
        {
            SetFiring(false);
            if (_pool == null) return;
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && _pool[i].IsActive) _pool[i].CancelForRound();
        }

        private void Update()
        {
            if (!IsFiring || !_hasDirection || _tuning == null) return;

            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f) return;
            _cooldown += _tuning.TurretFireIntervalSeconds;

            Projectile bullet = TakeFreeBullet();
            if (bullet == null) return; // 池全滿：這一發不算發射，免得「發射數」與戰果對不齊
            bullet.Fire(_transform.position, _direction);
            ShotsFired++;
        }

        private Projectile TakeFreeBullet()
        {
            if (_pool == null || _pool.Length == 0) return null;
            for (int i = 0; i < _pool.Length; i++)
            {
                int slot = (_nextSlot + i) % _pool.Length;
                if (_pool[slot] == null || _pool[slot].IsActive) continue;
                _nextSlot = (slot + 1) % _pool.Length;
                return _pool[slot];
            }
            return null;
        }

        private int Sum(int which)
        {
            if (_pool == null) return 0;
            int total = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] == null) continue;
                if (which == 0) total += _pool[i].Penetrations;
                else if (which == 1) total += _pool[i].Blocks;
                else total += _pool[i].TargetHits;
            }
            return total;
        }
    }
}
