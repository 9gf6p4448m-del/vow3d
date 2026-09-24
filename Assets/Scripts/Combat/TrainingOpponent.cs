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

        // ── v0.8.0 佔領模式（V080_CAPTURE_PLAN.md §2.2「對手 AI」、E9／E10／E24）──
        // 旗標關著時 Update 走的是原本的單挑分支，一行都沒改；旗標只由 Phase1Bootstrap 在 CAPTURE 鈕進出模式時切換。
        private ICaptureMatchView _captureView;
        private CaptureTuning _captureTuning;
        private readonly int[] _ownershipBuffer = new int[HexBoardLayout.TileCount];
        private Renderer[] _bodyRenderers;
        private bool _captureMode;
        private bool _chasingHero;
        private int _targetTile = -1;

        public bool IsCaptureMode => _captureMode;
        public bool IsChasingHero => _chasingHero;
        public int CaptureTargetTile => _targetTile;
        public bool IsBodyHidden { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _locomotion = GetComponent<HeroLocomotion>();
            _bodyRenderers = GetComponentsInChildren<Renderer>(true);
        }

        public void ConfigureCapture(ICaptureMatchView view, CaptureTuning tuning)
        {
            _captureView = view;
            _captureTuning = tuning;
        }

        public void SetCaptureMode(bool captureMode)
        {
            _captureMode = captureMode;
            _chasingHero = false;
            _targetTile = -1;
        }

        // 佔領開局與倒地復活共用（E18／E20）：傳送＋補滿血＋恢復 Renderer／Collider，並以「還沒在追、沒有目標塔」起步。
        public void RespawnAt(Vector3 position)
        {
            StopRound();
            _locomotion.WarpTo(position);
            Revive();
            SetBodyHidden(false);
            _phase = AttackPhase.Chase;
            _phaseRemaining = 0f;
            _chasingHero = false;
            _targetTile = -1;
            _active = _hero != null;
        }

        // 結算停頓結束回佔領待機（E17）：重用單挑的 ResetForRound（回出生點、補滿血），再把倒地時關掉的身體打開。
        public void ReturnToCaptureLobby()
        {
            ResetForRound();
            SetBodyHidden(false);
            _chasingHero = false;
            _targetTile = -1;
        }

        // 佔領對局倒地期間（E19）：不擋路、點不到、不能被鎖定。
        public void SetBodyHidden(bool hidden)
        {
            IsBodyHidden = hidden;
            for (int i = 0; i < _bodyRenderers.Length; i++)
                if (_bodyRenderers[i] != null) _bodyRenderers[i].enabled = !hidden;
            Collider[] colliders = TargetColliders;
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = !hidden;
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
            if (_captureMode)
            {
                UpdateCapture();
                return;
            }
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

        // ───────────────────── v0.8.0 佔領模式 ─────────────────────
        // 英雄倒地時照常搶點（R4：單挑的「英雄倒地整個停擺」只留在單挑分支）。決策只在追擊相位問
        // CaptureOpponentPolicy；決定追英雄就走與單挑相同的 Chase → Windup → Recovery，前搖與恢復一定跑完（E10）。
        private void UpdateCapture()
        {
            if (!_active || _frozen || _hero == null || _captureView == null || _captureTuning == null) return;
            float dt = Time.deltaTime;
            if (_phase == AttackPhase.Chase)
            {
                StepCaptureChase(dt);
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

            // 恢復結束：回到追擊相位，同一幀就重新評估一次（這時才可能放棄追打）。
            _phase = AttackPhase.Chase;
            StepCaptureChase(dt);
        }

        private void StepCaptureChase(float dt)
        {
            Vector3 self = transform.position;
            Vector3 heroPosition = _hero.transform.position;
            for (int i = 0; i < _ownershipBuffer.Length; i++) _ownershipBuffer[i] = (int)_captureView.OwnerOf(i);

            CaptureOpponentDecision decision = CaptureOpponentPolicy.Decide(
                self.x, self.z, heroPosition.x, heroPosition.z, !_hero.IsAlive,
                _chasingHero, _ownershipBuffer, _captureTuning);

            if (decision.ChaseHero)
            {
                // 剛轉頭、或上一段前搖把移動停掉（Stop 之後 HasArrived 恆真）：重新下追擊指令。
                if (!_chasingHero || _locomotion.HasArrived) _locomotion.Chase(_hero.transform);
                _chasingHero = true;
                _targetTile = -1;

                // 以下與單挑分支的追擊相位相同（v0.7.0 Chase → Windup）。
                Vector3 offset = heroPosition - self;
                offset.y = 0f;
                float stop = _tuning.OpponentStopDistance;
                if (offset.sqrMagnitude > stop * stop || HasBlockingWall(heroPosition))
                {
                    _locomotion.Step(dt);
                    return;
                }

                _locomotion.Stop();
                _locomotion.FaceTowards(heroPosition);
                _attackCenter = heroPosition;
                _attackCenter.y = 0f;
                _telegraph?.ShowZoneIndicator(_attackCenter, _tuning.AttackRadius, 0.09f);
                _phaseRemaining = _tuning.WindupSeconds;
                _phase = AttackPhase.Windup;
                return;
            }

            bool wasChasing = _chasingHero;
            _chasingHero = false;
            if (wasChasing || decision.TargetTile != _targetTile)
            {
                _targetTile = decision.TargetTile;
                IssueTileOrder(self.y);
            }
            else if (_targetTile >= 0 && _locomotion.HasArrived)
            {
                // 移動指令被外力清掉（例如被傳送）而且人不在該塔光圈內：補下一次，不然會原地發呆。
                float dx = HexBoardLayout.CenterX(_targetTile) - self.x;
                float dz = HexBoardLayout.CenterZ(_targetTile) - self.z;
                float radius = _captureTuning.CircleRadius;
                if (dx * dx + dz * dz > radius * radius) IssueTileOrder(self.y);
            }
            _locomotion.Step(dt);
        }

        private void IssueTileOrder(float groundY)
        {
            if (_targetTile < 0)
            {
                _locomotion.Stop();
                return;
            }
            _locomotion.MoveTo(new Vector3(HexBoardLayout.CenterX(_targetTile), groundY, HexBoardLayout.CenterZ(_targetTile)));
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
