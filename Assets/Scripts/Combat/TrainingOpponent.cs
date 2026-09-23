using UnityEngine;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    [RequireComponent(typeof(HeroLocomotion))]
    public sealed class TrainingOpponent : CombatTargetBehaviour, IHitstopParticipant
    {
        private enum AttackPhase { Chase, Windup, Recovery }

        private readonly RaycastHit[] _occlusionHits = new RaycastHit[12];
        private HeroLocomotion _locomotion;
        private HeroController _hero;
        private SkillTelegraphService _telegraph;
        private DuelTuning _tuning;
        private Vector3 _spawn;
        private Vector3 _attackCenter;
        private float _phaseRemaining;
        private AttackPhase _phase;
        private bool _active;
        private bool _frozen;

        public bool IsEngaged => _active;
        public bool IsWarning => _active && _phase == AttackPhase.Windup;
        public Vector3 AttackCenter => _attackCenter;
        public int AttacksResolved { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _locomotion = GetComponent<HeroLocomotion>();
        }

        public void Initialize(HeroController hero, SkillTelegraphService telegraph, DuelTuning tuning,
                               GridNavigator navigator, float bodyRadius)
        {
            _hero = hero;
            _telegraph = telegraph;
            _tuning = tuning;
            _spawn = transform.position;
            _locomotion.Configure(tuning.OpponentMoveSpeed, 1080f, bodyRadius);
            _locomotion.SetNavigator(navigator, bodyRadius);
            Configure(tuning.OpponentHealth, Faction.RedTeam);
            _active = false;
            _phase = AttackPhase.Chase;
            _telegraph?.HideIndicator();
        }

        public override bool CanBeTargetedBy(Faction attackerFaction)
        {
            return _active && base.CanBeTargetedBy(attackerFaction);
        }

        protected override bool CanReceiveDamage() { return _active; }

        public void StartRound()
        {
            if (!IsAlive || _hero == null) return;
            _active = true;
            _phase = AttackPhase.Chase;
            _phaseRemaining = 0f;
            _locomotion.Chase(_hero.transform);
        }

        public void StopRound()
        {
            _active = false;
            _locomotion.Stop();
            _telegraph?.HideIndicator();
        }

        public void ResetForRound()
        {
            StopRound();
            _locomotion.WarpTo(_spawn);
            Revive();
            _phase = AttackPhase.Chase;
            _phaseRemaining = 0f;
        }

        protected override void HandleDeath() { StopRound(); }

        public void SetHitstopFrozen(bool frozen) { _frozen = frozen; }

        private void Update()
        {
            if (!_active || _frozen || _hero == null || !_hero.IsAlive) return;
            float dt = Time.deltaTime;
            if (_phase == AttackPhase.Chase)
            {
                Vector3 offset = _hero.transform.position - transform.position;
                offset.y = 0f;
                float stop = _tuning.OpponentStopDistance;
                if (offset.sqrMagnitude > stop * stop || HasBlockingWall(_hero.transform.position))
                {
                    _locomotion.Step(dt);
                    return;
                }

                _locomotion.Stop();
                _locomotion.FaceTowards(_hero.transform.position);
                _attackCenter = _hero.transform.position;
                _attackCenter.y = 0f;
                _telegraph?.ShowZoneIndicator(_attackCenter, _tuning.AttackRadius, 0.09f);
                _phaseRemaining = _tuning.WindupSeconds;
                _phase = AttackPhase.Windup;
                return;
            }

            _phaseRemaining -= dt;
            if (_phaseRemaining > 0f) return;
            if (_phase == AttackPhase.Windup)
            {
                _telegraph?.HideIndicator();
                ResolveAttack();
                AttacksResolved++;
                _phase = AttackPhase.Recovery;
                _phaseRemaining = _tuning.RecoverySeconds;
                return;
            }

            _phase = AttackPhase.Chase;
            _locomotion.Chase(_hero.transform);
        }

        private void ResolveAttack()
        {
            if (!_hero.IsAlive) return;
            Vector3 target = _hero.transform.position;
            float dx = target.x - _attackCenter.x;
            float dz = target.z - _attackCenter.z;
            float radius = _tuning.AttackRadius;
            if (dx * dx + dz * dz > radius * radius || HasBlockingWall(target)) return;
            _hero.TakeDuelDamage(_tuning.OpponentDamage);
        }

        private bool HasBlockingWall(Vector3 target)
        {
            Vector3 start = transform.position + Vector3.up * 0.9f;
            Vector3 end = target + Vector3.up * 0.9f;
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(start, delta / distance, _occlusionHits, distance,
                                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= _occlusionHits.Length)
            {
                Debug.LogWarning("[VOW] 對手攻擊的牆遮擋射線緩衝已滿，取消這次命中。", this);
                return true;
            }
            for (int i = 0; i < count; i++)
            {
                CombatTargetBehaviour wall = _occlusionHits[i].collider.GetComponentInParent<CombatTargetBehaviour>();
                if (wall != null && wall.IsAlive && (wall is RuneWall || wall is TestWallTarget)) return true;
            }
            return false;
        }
    }
}
