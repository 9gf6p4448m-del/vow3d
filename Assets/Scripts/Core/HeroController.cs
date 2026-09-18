using System;
using UnityEngine;
using Vow.Core.Logic;

namespace Vow.Core
{
    // 英雄的組裝點：把輸入事件翻譯成大腦指令，並以 IHeroBodyPort 的身分替大腦執行物理動作。
    // 本類別不含任何節奏判斷——窗口、緩衝、佇列、攻擊週期全部在 HeroCombatBrain（純邏輯、有測試）。
    [RequireComponent(typeof(HeroLocomotion))]
    [RequireComponent(typeof(MicroCadenceMover))]
    public sealed class HeroController : MonoBehaviour, IHeroBodyPort<ICombatTarget>, IAttackHitReceiver
    {
        [SerializeField] private HeroTuningAsset _tuningAsset;
        [SerializeField] private Faction _faction = Faction.BlueTeam;

        private HeroTuningAsset _tuning;
        private HeroLocomotion _locomotion;
        private MicroCadenceMover _mover;
        private PlayerStateMachine _stateMachine;
        private HeroCombatBrain<ICombatTarget> _brain;

        private IPlayerInputService _input;
        private ICombatFeedbackService _feedback;
        private IHapticService _haptics;
        private Transform _cameraTransform;
        private bool _watchdogReported;

        private static readonly Color KillFlashColor = new Color(1f, 1f, 1f, 0.3f);

        public IPlayerStateMachine StateMachine => _stateMachine;
        public ICadenceMover CadenceMover => _mover;
        public MicroCadenceMover Mover => _mover;
        public Faction HeroFaction => _faction;
        public ICombatTarget CurrentTarget => _brain != null ? _brain.CurrentTarget : null;
        public float CadenceWindowRemainingNormalized => _brain != null ? _brain.CadenceWindowRemainingNormalized : 0f;
        public float AttackRange => _tuning != null ? _tuning.AttackRange : 0f;

        public event Action OnAttackWindupStarted;
        public event Action<ICombatTarget> OnAttackHitResolved;

        private void Awake()
        {
            _tuning = _tuningAsset != null ? _tuningAsset : ScriptableObject.CreateInstance<HeroTuningAsset>();

            _locomotion = GetComponent<HeroLocomotion>();
            _mover = GetComponent<MicroCadenceMover>();
            _locomotion.Configure(_tuning.MoveSpeed, _tuning.TurnSpeedDegreesPerSecond, _tuning.BodyRadius);
            _mover.Configure(_tuning.Combat);

            _stateMachine = new PlayerStateMachine();
            _stateMachine.OnTransitionRejected += HandleTransitionRejected;

            _brain = new HeroCombatBrain<ICombatTarget>(_stateMachine, this, _tuning.Combat);
            _brain.OnAttackWindupStarted += HandleWindupStarted;
            _brain.OnAttackHitResolved += HandleHitResolved;
            _brain.OnWindupWatchdogFired += HandleWatchdogFired;
            _mover.OnDashExecuted += HandleDashExecuted;
        }

        // 由 Phase1Bootstrap 注入依賴（不在這裡 Find，任何一項都可以換成測試替身）。
        public void Initialize(IPlayerInputService input, ICombatFeedbackService feedback, Camera viewCamera,
            IHapticService haptics = null)
        {
            Unsubscribe();
            _input = input;
            _feedback = feedback;
            _haptics = haptics;
            _cameraTransform = viewCamera != null ? viewCamera.transform : null;

            if (_input == null) return;
            _input.OnMoveDestinationSelected += HandleMoveSelected;
            _input.OnCombatTargetSelected += HandleTargetSelected;
            _input.OnCadenceVectorFlicked += HandleCadenceFlick;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_input == null) return;
            _input.OnMoveDestinationSelected -= HandleMoveSelected;
            _input.OnCombatTargetSelected -= HandleTargetSelected;
            _input.OnCadenceVectorFlicked -= HandleCadenceFlick;
            _input = null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _locomotion.Step(dt);
            _mover.Step(dt);
            _brain.Tick(dt);
        }

        // ───────────────────────── 輸入 → 指令 ─────────────────────────

        private void HandleMoveSelected(Vector3 destination)
        {
            _brain.CommandMove(new GroundPoint(destination.x, destination.y, destination.z));
        }

        private void HandleTargetSelected(ICombatTarget target)
        {
            if (target == null || !target.CanBeTargetedBy(_faction)) return;
            _brain.CommandAttack(target);
        }

        // 螢幕向量 → 世界 XZ 方向（以鏡頭水平朝向為基準，玩家往螢幕哪邊彈，角色就往畫面上的那邊滑）。
        private void HandleCadenceFlick(Vector2 screenDirection)
        {
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (_cameraTransform != null)
            {
                forward = _cameraTransform.forward;
                right = _cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;
                if (forward.sqrMagnitude < 1e-6f) forward = _cameraTransform.up; // 純俯視鏡頭
                forward.y = 0f;
                forward.Normalize();
                right.Normalize();
            }

            Vector3 world = right * screenDirection.x + forward * screenDirection.y;
            _brain.CommandFlick(world.x, world.z);
        }

        // 動畫事件 OnAttackHit() 的落點（經 HeroAnimationDriver 轉送）。
        public void NotifyAttackHit()
        {
            _brain.NotifyAttackHit();
        }

        // ───────────────────────── IHeroBodyPort ─────────────────────────

        public bool HasArrivedAtDestination => _locomotion.HasArrived;
        public bool IsCadenceDashComplete => !_mover.IsDashing;

        public bool IsTargetValid(ICombatTarget target)
        {
            return target != null && target.IsAlive && target.TargetTransform != null
                   && target.CanBeTargetedBy(_faction);
        }

        public bool IsTargetInAttackRange(ICombatTarget target)
        {
            if (target == null) return false;
            Transform targetTransform = target.TargetTransform;
            if (targetTransform == null) return false;

            Vector3 offset = targetTransform.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= _tuning.AttackRange * _tuning.AttackRange;
        }

        public void MoveTo(GroundPoint destination)
        {
            _locomotion.MoveTo(new Vector3(destination.X, destination.Y, destination.Z));
        }

        public void ChaseTarget(ICombatTarget target)
        {
            _locomotion.Chase(target.TargetTransform);
        }

        public void StopMoving()
        {
            _locomotion.Stop();
        }

        public void FaceTarget(ICombatTarget target)
        {
            Transform targetTransform = target.TargetTransform;
            if (targetTransform != null) _locomotion.FaceTowards(targetTransform.position);
        }

        // 打擊反饋金字塔（GDD §肆-2）：普通平 A＝頓挫＋微震＋輕震覺；斬殺＝再加瞬時閃白與焦痕貼花；破牆＝重震＋重震覺＋地裂貼花。
        public void ResolveAttackHit(ICombatTarget target)
        {
            target.ReceiveDamage(_tuning.AttackDamage, DamageType.Physical, gameObject);

            bool killed = !target.IsAlive;
            bool wallBroken = killed && target.TargetFaction == Faction.DestructibleWall;
            if (_haptics != null) _haptics.Notify(wallBroken ? HapticCue.WallBreak : HapticCue.BasicAttackHit);
            if (_feedback == null) return;

            _feedback.TriggerHitstop(_tuning.HitstopMilliseconds);
            Transform targetTransform = target.TargetTransform;

            if (wallBroken)
            {
                _feedback.RequestCameraShake(_tuning.WallBreakTrauma, 0.45f);
                if (targetTransform != null) _feedback.SpawnGroundDecal(targetTransform.position, DecalType.VoidRupture);
                return;
            }

            _feedback.RequestCameraShake(_tuning.BasicAttackTrauma, 0.15f);
            if (!killed) return;

            _feedback.TriggerScreenFlash(KillFlashColor, 60f);
            if (targetTransform != null) _feedback.SpawnGroundDecal(targetTransform.position, DecalType.ScorchCrater);
        }

        public bool TryBeginCadenceDash(float worldDirX, float worldDirZ)
        {
            return _mover.TryExecuteCadenceDash(new Vector3(worldDirX, 0f, worldDirZ));
        }

        // ───────────────────────── 大腦事件 ─────────────────────────

        private void HandleDashExecuted(float distance)
        {
            if (_haptics != null) _haptics.Notify(HapticCue.CadenceDash);
        }

        private void HandleWindupStarted()
        {
            OnAttackWindupStarted?.Invoke();
        }

        private void HandleHitResolved(ICombatTarget target)
        {
            OnAttackHitResolved?.Invoke(target);
        }

        private void HandleTransitionRejected(PlayerState from, PlayerState to)
        {
            Debug.LogError("[VOW] 非法狀態轉換被拒絕：" + from + " → " + to, this);
        }

        private void HandleWatchdogFired()
        {
            if (_watchdogReported) return;
            _watchdogReported = true;
            Debug.LogError("[VOW] 前搖逾時仍未收到動畫事件 OnAttackHit()，已由保險機制強制結算。" +
                           "請檢查 Attack 動畫切片是否掛了 OnAttackHit 事件（執行 VOW/Phase 1/Build Greybox Scene 會自動補上）。", this);
        }
    }
}
