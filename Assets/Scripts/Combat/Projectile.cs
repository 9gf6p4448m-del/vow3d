using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 一發預建的直線子彈（Phase 2 批 3）。池化、執行期零配置、沒有 Collider、不進 NavGrid。
    //
    // 為什麼一定要掃掠而不是查當幀端點（§4-5）：速度 20 m/s、牆厚 0.6m，每幀位移在 30fps 就已經是 0.667m，
    // 手機瀏覽器低於 33fps 是常態 → 點查詢必然漏穿。每幀改查「上一幀位置 → 這一幀位置」這條線段。
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField] private Renderer _visual;

        private ProjectileTuning _tuning;
        private ProjectileFlightLogic _flight;
        private ICombatTargetResolver _resolver;
        private Faction _ownerFaction;
        private Transform _transform;

        private RaycastHit[] _sweepBuffer;
        private SweepHit[] _sweepHits;
        private ICombatTarget[] _sweepTargets;   // 與 _sweepHits 平行（排序前的填入順序），排序後用 Id 回查

        private Vector3 _position;
        private Vector3 _direction;
        private float _travelled;
        private float _remainingLife;
        private bool _active;

        public bool IsActive => _active;

        // 活性計數（V4-n／V5 用）。池化重用時累加，不重設。
        public int Penetrations { get; private set; }
        public int Blocks { get; private set; }
        public int TargetHits { get; private set; }
        public int Expiries { get; private set; }

        private void Awake()
        {
            _transform = transform;
            if (_visual == null) _visual = GetComponent<Renderer>();
            SetVisible(false);
        }

        public void Initialize(ProjectileTuning tuning, ICombatTargetResolver resolver, Faction ownerFaction)
        {
            _tuning = tuning;
            _resolver = resolver;
            _ownerFaction = ownerFaction;

            if (_flight == null) _flight = new ProjectileFlightLogic(tuning.PenetratedWallBufferSize);
            if (_sweepBuffer == null || _sweepBuffer.Length != tuning.SweepHitBufferSize)
            {
                _sweepBuffer = new RaycastHit[tuning.SweepHitBufferSize];
                _sweepHits = new SweepHit[tuning.SweepHitBufferSize];
                _sweepTargets = new ICombatTarget[tuning.SweepHitBufferSize];
            }
            Despawn();
        }

        public void Fire(Vector3 origin, Vector3 direction)
        {
            if (_tuning == null) return;

            _flight.Reset();
            _position = origin;
            _direction = direction.normalized;
            _travelled = 0f;
            _remainingLife = _tuning.BulletLifespanSeconds;
            _active = true;
            _transform.position = _position;
            SetVisible(true);
        }

        private void Update()
        {
            if (!_active || _tuning == null) return;

            float dt = Time.deltaTime;
            float step = ProjectileFlightLogic.StepLength(_tuning.BulletSpeed, dt);
            if (step > 0f && ResolveSweep(step)) return; // 已結算並收回池裡

            _position += _direction * step;
            _travelled += step;
            _remainingLife -= dt;
            _transform.position = _position;

            if (_travelled < _tuning.BulletMaxRange && _remainingLife > 0f) return;
            Expiries++;
            Despawn();
        }

        // 回傳 true＝這一發已經結算完畢（擋下或命中）。
        private bool ResolveSweep(float step)
        {
            int rawCount = Physics.RaycastNonAlloc(new Ray(_position, _direction), _sweepBuffer, step,
                                                   Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (rawCount <= 0) return false;
            if (rawCount > _sweepBuffer.Length) rawCount = _sweepBuffer.Length;

            // 非 ICombatTarget 的命中（地板、英雄、邊界、疊圖）＝ SweepHitKind.Ignore：直接不收進來、續飛。
            int count = 0;
            for (int i = 0; i < rawCount; i++)
            {
                if (_resolver == null) break;
                if (!_resolver.TryResolve(_sweepBuffer[i].collider, out ICombatTarget target)) continue;
                if (target == null || !target.IsAlive || target.TargetTransform == null) continue;

                _sweepHits[count].Distance = _sweepBuffer[i].distance;
                _sweepHits[count].Id = target.TargetTransform.GetInstanceID();
                _sweepHits[count].Kind = Classify(target);
                _sweepTargets[count] = target;
                count++;
            }
            if (count == 0) return false;

            int unsortedCount = count;
            ProjectileFlightLogic.SortByDistance(_sweepHits, count); // 這裡的順序決定結果：近的牆先擋、先穿

            for (int i = 0; i < count; i++)
            {
                ICombatTarget target = FindTarget(_sweepHits[i].Id, unsortedCount);
                if (target == null) continue;

                if (_sweepHits[i].Kind == SweepHitKind.FriendlyWall)
                {
                    PenetrateFriendlyWall(target, _sweepHits[i].Id);
                    continue; // 己方牆一律續飛（穿得過就帶著衰減，穿不動也不擋自己人，§4-9）
                }

                // 敵方／中立石牆擋下，或命中一般目標：都在命中點結算並結束這一發
                _position += _direction * _sweepHits[i].Distance;
                _transform.position = _position;
                target.ReceiveDamage(_tuning.BulletDamage * _flight.DamageMultiplier, DamageType.Physical, gameObject);
                if (_sweepHits[i].Kind == SweepHitKind.BlockingWall) Blocks++;
                else TargetHits++;
                Despawn();
                return true;
            }
            return false;
        }

        private void PenetrateFriendlyWall(ICombatTarget target, int wallId)
        {
            if (_flight.HasPenetrated(wallId)) return; // 同一發子彈對同一面牆只算一次（跨幀也是）

            IRuneWall wall = target as IRuneWall;
            if (wall == null) return;

            if (wall.TryPenetrateBullet(_direction * _tuning.BulletSpeed, out float multiplier))
            {
                _flight.RecordPenetration(wallId, multiplier);
                Penetrations++;
                return;
            }
            // 穿不動（牆已死或已達全隊穿透上限）：續飛且倍率不再變動，但仍登記名冊避免這一發重複嘗試
            _flight.RecordPenetration(wallId, 1f);
        }

        private SweepHitKind Classify(ICombatTarget target)
        {
            if (target.TargetFaction != Faction.DestructibleWall) return SweepHitKind.Target;

            IFactionOwned owned = target as IFactionOwned;
            if (owned != null && owned.OwnerFaction == _ownerFaction) return SweepHitKind.FriendlyWall;
            return SweepHitKind.BlockingWall;
        }

        // 排序只搬 SweepHit，所以要用 Id 把目標回查出來（n ≤ 8，零配置）。同一個目標不會出現兩個 Id。
        private ICombatTarget FindTarget(int id, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ICombatTarget candidate = _sweepTargets[i];
                if (candidate == null || candidate.TargetTransform == null) continue;
                if (candidate.TargetTransform.GetInstanceID() == id) return candidate;
            }
            return null;
        }

        private void Despawn()
        {
            _active = false;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_visual != null) _visual.enabled = visible;
        }
    }
}
