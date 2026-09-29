using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 場景預建的單一目標：待命、先鋒、核心與巨獸共用一個 CombatTargetBehaviour 血量帳。
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class AbyssalVanguardTarget : CombatTargetBehaviour, IGlobalObjectiveVisibility
    {
        public enum ObjectivePhase { Inactive, Vanguard, Core, Behemoth }

        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer _coreRenderer;
        [SerializeField] private Renderer _warningRenderer;
        [SerializeField] private Material _neutralMaterial;
        [SerializeField] private Material _blueMaterial;
        [SerializeField] private Material _redMaterial;

        private readonly RaycastHit[] _moveHits = new RaycastHit[12];
        private CapsuleCollider _bodyCollider;
        private TargetOverheadDisplay _overhead;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private CaptureBoardSpec _board;
        private GridNavigator _navigator;
        private CombatTargetBehaviour[] _walls;
        private AbyssalVanguardTuning _tuning;
        private int[] _route;
        private int[] _parents;
        private int[] _queue;
        private int _routeLength;
        private int _routeIndex;
        private float _goalX;
        private float _goalZ;
        private float _goalRefresh;
        private float _counterattackTimer;
        private float _warningRemaining;
        private float _attackTimer;
        private bool _fogVisible = true;

        public ObjectivePhase Phase { get; private set; }
        public int BehemothSide { get; private set; } = -1;
        public bool IsFogVisible => _fogVisible;
        public bool IsWarning => Phase == ObjectivePhase.Vanguard && _warningRemaining > 0f;
        public int DestinationTile => _routeLength > 0 ? _route[_routeLength - 1] : -1;
        public bool HasReachedDestination => Phase == ObjectivePhase.Behemoth && _routeLength > 0 && _routeIndex >= _routeLength;

        protected override void Awake()
        {
            base.Awake();
            _bodyCollider = GetComponent<CapsuleCollider>();
            _overhead = GetComponent<TargetOverheadDisplay>();
            // FindObjectsOfType 必須找到啟用中的根；待機生命為 0，且所有實體碰撞與外觀關閉。
            Deactivate();
        }

        public void Initialize(HeroController hero, TrainingOpponent opponent, CaptureBoardSpec board,
                               GridNavigator navigator, float bodyRadius, CombatTargetBehaviour[] walls,
                               AbyssalVanguardTuning tuning)
        {
            _hero = hero;
            _opponent = opponent;
            _board = board;
            _navigator = navigator;
            _walls = walls;
            _tuning = tuning;
            int tileCount = board != null ? board.TileCount : 0;
            _route = new int[tileCount];
            _parents = new int[tileCount];
            _queue = new int[tileCount];
            if (_bodyCollider == null) _bodyCollider = GetComponent<CapsuleCollider>();
            _bodyCollider.radius = Mathf.Max(0.1f, bodyRadius);
            Deactivate();
        }

        public void ActivateVanguard()
        {
            if (_tuning == null || _board == null) return;
            Phase = ObjectivePhase.Vanguard;
            BehemothSide = -1;
            transform.position = new Vector3(_tuning.CoreX, 0f, _tuning.CoreZ);
            Configure(_tuning.VanguardMaxHealth, Faction.Neutral);
            SetOwnerFaction(Faction.Neutral);
            _counterattackTimer = _tuning.VanguardCounterattackIntervalSeconds;
            _warningRemaining = 0f;
            _routeLength = 0;
            _routeIndex = 0;
            _fogVisible = true;
            RefreshAppearance();
        }

        public void ShowCore()
        {
            if (Phase != ObjectivePhase.Vanguard && Phase != ObjectivePhase.Core) return;
            Phase = ObjectivePhase.Core;
            _warningRemaining = 0f;
            RefreshAppearance();
        }

        public void ActivateBehemoth(int side)
        {
            if (_tuning == null || _board == null ||
                (side != CaptureMatchLogic.BlueFactionId && side != CaptureMatchLogic.RedFactionId)) return;
            BehemothSide = side;
            Phase = ObjectivePhase.Behemoth;
            Faction owner = side == CaptureMatchLogic.BlueFactionId ? Faction.BlueTeam : Faction.RedTeam;
            Configure(_tuning.BehemothMaxHealth, owner);
            SetOwnerFaction(owner);
            _attackTimer = _tuning.BehemothAttackIntervalSeconds;
            _goalRefresh = 0f;
            BuildRoute(_board.TileAt(transform.position.x, transform.position.z),
                       _board.MotherTile(OpposingSide(side), 0));
            _fogVisible = owner == Faction.BlueTeam;
            RefreshAppearance();
        }

        public void Deactivate()
        {
            Phase = ObjectivePhase.Inactive;
            BehemothSide = -1;
            _routeLength = 0;
            _routeIndex = 0;
            _warningRemaining = 0f;
            _fogVisible = true;
            Configure(0f, Faction.Neutral);
            SetOwnerFaction(Faction.Neutral);
            RefreshAppearance();
        }

        public void SetFogVisible(bool visible)
        {
            if (_fogVisible == visible) return;
            _fogVisible = visible;
            RefreshAppearance();
        }

        public bool IsGloballyVisibleTo(Faction viewer)
        {
            return Phase == ObjectivePhase.Vanguard || Phase == ObjectivePhase.Core ||
                   (Phase == ObjectivePhase.Behemoth && viewer == OwnerFaction);
        }

        public override bool CanBeTargetedBy(Faction attackerFaction)
        {
            return (Phase == ObjectivePhase.Vanguard || Phase == ObjectivePhase.Behemoth)
                   && base.CanBeTargetedBy(attackerFaction);
        }

        protected override bool CanReceiveDamage()
        {
            return Phase == ObjectivePhase.Vanguard || Phase == ObjectivePhase.Behemoth;
        }

        protected override void HandleDeath()
        {
            if (Phase == ObjectivePhase.Vanguard) ShowCore();
            else if (Phase == ObjectivePhase.Behemoth) Deactivate();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Phase == ObjectivePhase.Vanguard) TickVanguard(dt);
            else if (Phase == ObjectivePhase.Behemoth) TickBehemoth(dt);
        }

        private void TickVanguard(float dt)
        {
            _counterattackTimer -= dt;
            if (_warningRemaining > 0f)
            {
                _warningRemaining -= dt;
                if (_warningRemaining <= 0f)
                {
                    _warningRemaining = 0f;
                    StrikeNearbyHeroes(_tuning.VanguardCounterattackRadius, _tuning.VanguardCounterattackDamage);
                    RefreshAppearance();
                }
                return;
            }
            if (_counterattackTimer > 0f) return;
            _counterattackTimer = _tuning.VanguardCounterattackIntervalSeconds;
            _warningRemaining = _tuning.VanguardTelegraphSeconds;
            RefreshAppearance();
        }

        private void TickBehemoth(float dt)
        {
            MoveAlongRoute(dt);
            _attackTimer -= dt;
            if (_attackTimer > 0f) return;
            _attackTimer = _tuning.BehemothAttackIntervalSeconds;

            // 每輪攻擊各挑一個範圍內的敵英雄與敵牆，傷害入口沿用兩者既有的正式入口。
            const float reach = 2.5f;
            Vector3 here = transform.position;
            if (BehemothSide == CaptureMatchLogic.BlueFactionId && _opponent != null &&
                _opponent.IsAlive && _opponent.IsEngaged && HorizontalDistanceSquared(here, _opponent.transform.position) <= reach * reach)
                _opponent.ReceiveDamage(_tuning.BehemothHeroDamage, DamageType.Physical, gameObject);
            else if (BehemothSide == CaptureMatchLogic.RedFactionId && _hero != null &&
                     _hero.IsAlive && HorizontalDistanceSquared(here, _hero.transform.position) <= reach * reach)
                _hero.TakeDuelDamage(_tuning.BehemothHeroDamage, DamageType.Physical);

            if (_walls == null) return;
            CombatTargetBehaviour nearest = null;
            float nearestDistance = reach * reach;
            Faction friendly = BehemothSide == CaptureMatchLogic.BlueFactionId ? Faction.BlueTeam : Faction.RedTeam;
            for (int i = 0; i < _walls.Length; i++)
            {
                CombatTargetBehaviour wall = _walls[i];
                if (wall == null || !wall.IsAlive || wall == this ||
                    !(wall is RuneWall || wall is TestWallTarget) || wall.OwnerFaction == friendly) continue;
                float distance = HorizontalDistanceSquared(here, wall.transform.position);
                if (distance > nearestDistance) continue;
                nearestDistance = distance;
                nearest = wall;
            }
            if (nearest != null) nearest.ReceiveDamage(_tuning.BehemothWallDamage, DamageType.Physical, gameObject);
        }

        private void StrikeNearbyHeroes(float radius, float damage)
        {
            Vector3 here = transform.position;
            float radiusSq = radius * radius;
            if (_hero != null && _hero.IsAlive && HorizontalDistanceSquared(here, _hero.transform.position) <= radiusSq)
                _hero.TakeDuelDamage(damage, DamageType.Physical);
            if (_opponent != null && _opponent.IsAlive && _opponent.IsEngaged &&
                HorizontalDistanceSquared(here, _opponent.transform.position) <= radiusSq)
                _opponent.ReceiveDamage(damage, DamageType.Physical, gameObject);
        }

        private void MoveAlongRoute(float dt)
        {
            if (_routeIndex >= _routeLength || _board == null) return;
            int tile = _route[_routeIndex];
            Vector3 here = transform.position;
            float destX = _board.CenterX(tile);
            float destZ = _board.CenterZ(tile);
            if (HorizontalDistanceSquared(here, new Vector3(destX, here.y, destZ)) < 0.6f * 0.6f)
            {
                _routeIndex++;
                _goalRefresh = 0f;
                return;
            }

            _goalRefresh -= dt;
            if (_goalRefresh <= 0f)
            {
                _goalRefresh = 0.25f;
                if (_navigator != null)
                    _navigator.ResolveGoal(here.x, here.z, destX, destZ, out _goalX, out _goalZ, out _);
                else { _goalX = destX; _goalZ = destZ; }
            }
            float dx = _goalX - here.x;
            float dz = _goalZ - here.z;
            if (_navigator != null)
            {
                SteerMode mode = _navigator.Steer(here.x, here.z, _goalX, _goalZ, out float steerX, out float steerZ);
                if (mode == SteerMode.Stuck || mode == SteerMode.GoalBlocked) return;
                dx = steerX;
                dz = steerZ;
            }
            Vector3 direction = new Vector3(dx, 0f, dz);
            float length = direction.magnitude;
            if (length < 0.001f) return;
            direction /= length;
            float step = Mathf.Min(_tuning.BehemothMoveSpeed * dt, Mathf.Sqrt(HorizontalDistanceSquared(here,
                new Vector3(destX, here.y, destZ))));
            // NavGrid 處理繞路；實際位移再用實體碰撞裁切，避免低幀率穿牆。
            int count = Physics.SphereCastNonAlloc(here + Vector3.up, 0.48f, direction, _moveHits, step + 0.04f,
                                                   Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = _moveHits[i].collider;
                if (hit == null || hit == _bodyCollider || hit.transform.IsChildOf(transform)) continue;
                if (BehemothSide == CaptureMatchLogic.BlueFactionId && _hero != null &&
                    hit.transform.IsChildOf(_hero.transform)) continue;
                if (BehemothSide == CaptureMatchLogic.RedFactionId && _opponent != null &&
                    hit.transform.IsChildOf(_opponent.transform)) continue;
                if (_moveHits[i].distance < step + 0.04f)
                    step = Mathf.Max(0f, _moveHits[i].distance - 0.04f);
            }
            transform.position = here + direction * step;
            if (step > 0f) transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private void BuildRoute(int start, int destination)
        {
            _routeLength = 0;
            _routeIndex = 0;
            if (_board == null || start < 0 || destination < 0) return;
            for (int i = 0; i < _parents.Length; i++) _parents[i] = -1;
            int read = 0;
            int write = 0;
            _queue[write++] = start;
            _parents[start] = start;
            while (read < write && _parents[destination] < 0)
            {
                int tile = _queue[read++];
                for (int i = 0; i < _board.NeighborCount(tile); i++)
                {
                    int neighbor = _board.Neighbor(tile, i);
                    if (_parents[neighbor] >= 0) continue;
                    _parents[neighbor] = tile;
                    _queue[write++] = neighbor;
                }
            }
            if (_parents[destination] < 0) return;
            int cursor = destination;
            while (cursor != start)
            {
                _route[_routeLength++] = cursor;
                cursor = _parents[cursor];
            }
            for (int left = 0, right = _routeLength - 1; left < right; left++, right--)
            {
                int tile = _route[left];
                _route[left] = _route[right];
                _route[right] = tile;
            }
        }

        private void RefreshAppearance()
        {
            bool body = (Phase == ObjectivePhase.Vanguard || Phase == ObjectivePhase.Behemoth) && _fogVisible;
            if (_bodyRenderer != null)
            {
                _bodyRenderer.enabled = body;
                if (Phase == ObjectivePhase.Vanguard) _bodyRenderer.sharedMaterial = _neutralMaterial;
                else if (BehemothSide == CaptureMatchLogic.BlueFactionId) _bodyRenderer.sharedMaterial = _blueMaterial;
                else if (BehemothSide == CaptureMatchLogic.RedFactionId) _bodyRenderer.sharedMaterial = _redMaterial;
            }
            if (_bodyCollider != null)
                _bodyCollider.enabled = Phase == ObjectivePhase.Vanguard || Phase == ObjectivePhase.Behemoth;
            if (_coreRenderer != null) _coreRenderer.enabled = Phase == ObjectivePhase.Core;
            if (_warningRenderer != null)
                _warningRenderer.enabled = Phase == ObjectivePhase.Vanguard && _warningRemaining > 0f && _fogVisible;
            if (_overhead != null) _overhead.SetHidden(!body);
        }

        private static int OpposingSide(int side)
        {
            return side == CaptureMatchLogic.BlueFactionId ? CaptureMatchLogic.RedFactionId : CaptureMatchLogic.BlueFactionId;
        }

        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
