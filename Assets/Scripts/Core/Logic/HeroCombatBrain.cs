using System;

namespace Vow.Core.Logic
{
    public struct GroundPoint
    {
        public float X;
        public float Y;
        public float Z;

        public GroundPoint(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }

    // 戰鬥大腦對「身體」的全部要求。Unity 端由 HeroController 實作，測試端由假身體實作。
    public interface IHeroBodyPort<TTarget> where TTarget : class
    {
        bool IsTargetValid(TTarget target);
        bool IsTargetInAttackRange(TTarget target);
        bool HasArrivedAtDestination { get; }
        bool IsCadenceDashComplete { get; }

        void MoveTo(GroundPoint destination);
        void ChaseTarget(TTarget target);
        void StopMoving();
        void FaceTarget(TTarget target);
        void ResolveAttackHit(TTarget target);

        // 充能由微位移執行器持有；回傳 false 代表無充能，大腦據此留在窗口內走常規收招。
        bool TryBeginCadenceDash(float worldDirX, float worldDirZ);
    }

    // 普攻節奏與指令佇列的決策核心。不含任何 UnityEngine 依賴，所有時間由 Tick(dt) 餵入，行為完全決定性。
    //
    // 指令處理總則（紅線 1：沒有任何狀態會「拒收」指令）：
    //   前搖中           → 移動指令立即打斷；攻擊指令同目標忽略、射程內異目標改鎖但不重置前搖、射程外異目標中斷去追
    //   目押窗口中       → flick 且有充能 = 0 幀切後搖；移動指令 = 提前關窗走 0.15s 收招後步行；攻擊指令排隊
    //   收招／滑步中     → 移動與攻擊指令排隊（後到覆蓋先到），動作結束立即執行
    public sealed class HeroCombatBrain<TTarget> where TTarget : class
    {
        private enum PendingKind { None, Move, Attack }

        private readonly PlayerStateMachine _stateMachine;
        private readonly IHeroBodyPort<TTarget> _body;
        private readonly CombatTuning _tuning;

        private double _clock;
        private float _stateElapsed;

        private TTarget _currentTarget;

        private PendingKind _pendingKind;
        private GroundPoint _pendingPoint;
        private TTarget _pendingTarget;

        private bool _hasBufferedFlick;
        private double _bufferedFlickTime;
        private float _bufferedDirX;
        private float _bufferedDirZ;

        private double _nextAttackReadyTime;
        private double _windupStartTime;

        public HeroCombatBrain(PlayerStateMachine stateMachine, IHeroBodyPort<TTarget> body, CombatTuning tuning)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _body = body ?? throw new ArgumentNullException(nameof(body));
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        }

        public double Clock => _clock;
        public TTarget CurrentTarget => _currentTarget;
        public PlayerState State => _stateMachine.CurrentState;

        // 目押窗口剩餘比例 1 → 0，非窗口期間為 0（給 HUD 進度條）。
        public float CadenceWindowRemainingNormalized
        {
            get
            {
                if (_stateMachine.CurrentState != PlayerState.AttackRelease) return 0f;
                if (_tuning.CadenceWindowSeconds <= 0f) return 0f;
                float remaining = 1f - _stateElapsed / _tuning.CadenceWindowSeconds;
                return remaining < 0f ? 0f : remaining;
            }
        }

        public event Action OnAttackWindupStarted;   // 動畫層據此從頭播放 Attack
        public event Action<TTarget> OnAttackHitResolved;
        public event Action OnWindupWatchdogFired;   // 動畫事件遺失，屬設定錯誤，外層必須大聲回報

        // ───────────────────────── 指令 ─────────────────────────

        public void CommandMove(GroundPoint destination)
        {
            switch (_stateMachine.CurrentState)
            {
                case PlayerState.Idle:
                case PlayerState.Moving:
                case PlayerState.AttackWindup:   // 前搖可被移動指令立即打斷；尚未命中，不結算傷害、不消耗攻擊週期
                    ClearPending();
                    _hasBufferedFlick = false;
                    ExecuteMove(destination);
                    break;

                case PlayerState.AttackRelease:
                    SetPendingMove(destination);
                    EnterRecovery();             // 常規走 A：提前關窗，0.15s 平滑收招後步行
                    break;

                case PlayerState.AttackRecovery:
                case PlayerState.CadenceDashing:
                    SetPendingMove(destination);
                    break;

                case PlayerState.CastingRune:
                    break;                       // Phase 2
            }
        }

        public void CommandAttack(TTarget target)
        {
            if (target == null || !_body.IsTargetValid(target)) return;

            switch (_stateMachine.CurrentState)
            {
                case PlayerState.Idle:
                case PlayerState.Moving:
                    ClearPending();
                    _currentTarget = target;
                    EngageCurrentTarget();
                    break;

                case PlayerState.AttackWindup:
                    if (ReferenceEquals(target, _currentTarget)) return;   // 狂點同一目標：無事發生，前搖不重置

                    if (_body.IsTargetInAttackRange(target))
                    {
                        // 射程內改鎖：只換目標、保留前搖進度。若在這裡重新起手，交替點兩個相鄰目標就能讓前搖無限重置、
                        // 永遠打不出去——等同變相的卡刀硬直（紅線 1）。
                        _currentTarget = target;
                        _body.FaceTarget(target);
                        return;
                    }

                    // 新目標在射程外：這一刀確實打不到它，中斷前搖去追
                    _currentTarget = target;
                    _hasBufferedFlick = false;
                    EngageCurrentTarget();
                    break;

                case PlayerState.AttackRelease:
                case PlayerState.AttackRecovery:
                case PlayerState.CadenceDashing:
                    _pendingKind = PendingKind.Attack;
                    _pendingTarget = target;
                    break;

                case PlayerState.CastingRune:
                    break;
            }
        }

        // worldDir 為世界座標 XZ 平面方向，不需正規化。
        public void CommandFlick(float worldDirX, float worldDirZ)
        {
            if ((double)worldDirX * worldDirX + (double)worldDirZ * worldDirZ < 1e-12) return;

            switch (_stateMachine.CurrentState)
            {
                case PlayerState.AttackRelease:
                    TryDash(worldDirX, worldDirZ);
                    break;

                case PlayerState.AttackWindup:
                    // 預輸入緩衝：先記下，命中那一刻再看它是否還在 120ms 內。
                    _hasBufferedFlick = true;
                    _bufferedFlickTime = _clock;
                    _bufferedDirX = worldDirX;
                    _bufferedDirZ = worldDirZ;
                    break;

                // Attack Frame Lock：其餘狀態一律忽略，微滑步不可對空甩。
            }
        }

        // 由動畫事件 OnAttackHit() 驅動。
        public void NotifyAttackHit()
        {
            if (_stateMachine.CurrentState != PlayerState.AttackWindup) return;
            if (_stateElapsed < _tuning.WindupSeconds * _tuning.MinWindupFractionForHit) return;
            ResolveHit();
        }

        // ───────────────────────── 時間推進 ─────────────────────────

        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            _clock += dt;
            _stateElapsed += dt;

            switch (_stateMachine.CurrentState)
            {
                case PlayerState.Idle:
                    if (_currentTarget != null) EngageCurrentTarget();
                    break;

                case PlayerState.Moving:
                    if (_currentTarget != null)
                    {
                        if (!_body.IsTargetValid(_currentTarget) || _body.IsTargetInAttackRange(_currentTarget))
                            EngageCurrentTarget();
                    }
                    else if (_body.HasArrivedAtDestination)
                    {
                        _body.StopMoving();
                        SetState(PlayerState.Idle);
                    }
                    break;

                case PlayerState.AttackWindup:
                    if (!_body.IsTargetValid(_currentTarget))
                    {
                        _currentTarget = null;
                        _hasBufferedFlick = false;
                        SetState(PlayerState.Idle);
                    }
                    else if (_stateElapsed >= _tuning.WindupWatchdogSeconds)
                    {
                        OnWindupWatchdogFired?.Invoke();
                        ResolveHit();
                    }
                    break;

                case PlayerState.AttackRelease:
                    if (_stateElapsed >= _tuning.CadenceWindowSeconds) EnterRecovery();
                    break;

                case PlayerState.AttackRecovery:
                    if (_stateElapsed >= _tuning.RecoverySeconds) ResolveAfterAction();
                    break;

                case PlayerState.CadenceDashing:
                    if (_body.IsCadenceDashComplete) ResolveAfterAction();
                    break;
            }
        }

        // ───────────────────────── 內部 ─────────────────────────

        private void ResolveHit()
        {
            TTarget target = _currentTarget;
            if (!_body.IsTargetValid(target))
            {
                _currentTarget = null;
                _hasBufferedFlick = false;
                SetState(PlayerState.Idle);
                return;
            }

            // 攻擊週期在「真的命中」時才入帳，被打斷的前搖不消耗週期；
            // 以前搖起手時刻為錨，所以無論之後切不切後搖，下一刀的最早起手時刻都一樣（紅線 2）。
            _nextAttackReadyTime = _windupStartTime + _tuning.AttackPeriodSeconds;

            SetState(PlayerState.AttackRelease);
            _body.ResolveAttackHit(target);
            OnAttackHitResolved?.Invoke(target);

            if (_hasBufferedFlick)
            {
                _hasBufferedFlick = false;
                if (_clock - _bufferedFlickTime <= _tuning.InputBufferSeconds)
                    TryDash(_bufferedDirX, _bufferedDirZ);
            }
        }

        private void TryDash(float dirX, float dirZ)
        {
            if (_body.TryBeginCadenceDash(dirX, dirZ)) SetState(PlayerState.CadenceDashing);
            // 無充能：留在窗口內，時間到自然走 0.15s 收招。不罰站、不硬直。
        }

        private void EnterRecovery()
        {
            SetState(PlayerState.AttackRecovery);
        }

        private void ResolveAfterAction()
        {
            PendingKind kind = _pendingKind;
            GroundPoint point = _pendingPoint;
            TTarget queuedTarget = _pendingTarget;
            ClearPending();

            if (kind == PendingKind.Move)
            {
                ExecuteMove(point);
                return;
            }

            if (kind == PendingKind.Attack) _currentTarget = queuedTarget;

            if (_currentTarget != null) EngageCurrentTarget();
            else SetState(PlayerState.Idle);
        }

        private void ExecuteMove(GroundPoint destination)
        {
            _currentTarget = null;
            _body.MoveTo(destination);
            SetState(PlayerState.Moving);
        }

        // 依目標現況決定：失效→待機；射程外→追；射程內且週期已到→起手；射程內但週期未到→原地等（期間任何指令照收）。
        private void EngageCurrentTarget()
        {
            TTarget target = _currentTarget;
            if (target == null || !_body.IsTargetValid(target))
            {
                _currentTarget = null;
                _body.StopMoving();
                SetState(PlayerState.Idle);
                return;
            }

            if (!_body.IsTargetInAttackRange(target))
            {
                _body.ChaseTarget(target);
                SetState(PlayerState.Moving);
                return;
            }

            _body.StopMoving();
            _body.FaceTarget(target);

            if (_clock >= _nextAttackReadyTime) BeginWindup();
            else SetState(PlayerState.Idle);
        }

        private void BeginWindup()
        {
            _windupStartTime = _clock;

            _hasBufferedFlick = false;
            SetState(PlayerState.AttackWindup);
            _stateElapsed = 0f;
            OnAttackWindupStarted?.Invoke();
        }

        private void SetState(PlayerState next)
        {
            if (_stateMachine.CurrentState == next) return;
            if (_stateMachine.TryChangeState(next)) _stateElapsed = 0f;
        }

        private void SetPendingMove(GroundPoint destination)
        {
            _pendingKind = PendingKind.Move;
            _pendingPoint = destination;
            _pendingTarget = null;
        }

        private void ClearPending()
        {
            _pendingKind = PendingKind.None;
            _pendingTarget = null;
        }
    }
}
