using System.Collections.Generic;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests
{
    internal sealed class FakeTarget
    {
        public bool Alive = true;
        public bool InRange = true;
        public int HitsTaken;
    }

    // 假身體：移動只記帳，但充能與滑步走真實的 CadenceSim——決定「能不能滑、滑多遠」的那段不用替身。
    internal sealed class FakeBody : IHeroBodyPort<FakeTarget>
    {
        public readonly CombatTuning Tuning;
        public CadenceSimState Sim;

        public int MoveToCount;
        public GroundPoint LastMoveDestination;
        public int ChaseCount;
        public bool Arrived;
        public readonly List<float> DashDistances = new List<float>();

        public FakeBody(CombatTuning tuning)
        {
            Tuning = tuning;
            Sim = CadenceSimState.CreateFull(tuning);
        }

        public bool IsTargetValid(FakeTarget target) => target != null && target.Alive;
        public bool IsTargetInAttackRange(FakeTarget target) => target != null && target.InRange;
        public bool HasArrivedAtDestination => Arrived;
        public bool IsCadenceDashComplete => !Sim.DashActive;

        public void MoveTo(GroundPoint destination)
        {
            MoveToCount++;
            LastMoveDestination = destination;
        }

        public void ChaseTarget(FakeTarget target) { ChaseCount++; }
        public void StopMoving() { }
        public void FaceTarget(FakeTarget target) { }
        public void ResolveAttackHit(FakeTarget target) { target.HitsTaken++; }

        public bool TryBeginCadenceDash(float worldDirX, float worldDirZ)
        {
            CadenceSimInput input = new CadenceSimInput { DashRequested = true, DirX = worldDirX, DirZ = worldDirZ };
            Sim = CadenceSim.SimulateStep(Sim, input, 0f, Tuning, out CadenceSimStepResult result);
            if (result.DashStarted) DashDistances.Add(result.StartedDistance);
            return result.DashStarted;
        }

        public void Tick(float dt)
        {
            Sim = CadenceSim.SimulateStep(Sim, default(CadenceSimInput), dt, Tuning, out CadenceSimStepResult _);
        }
    }

    // 以固定步長推進大腦，並模擬 Animator：前搖起手後 WindupSeconds 時送出 OnAttackHit() 動畫事件。
    internal sealed class BrainTestHarness
    {
        public const float TickSeconds = 1f / 120f;

        public readonly CombatTuning Tuning = new CombatTuning();
        public readonly PlayerStateMachine StateMachine = new PlayerStateMachine();
        public readonly FakeBody Body;
        public readonly HeroCombatBrain<FakeTarget> Brain;

        public readonly List<double> WindupStartTimes = new List<double>();
        public readonly List<string> Transitions = new List<string>();
        public int RejectedTransitions;
        public int WatchdogFires;

        public bool SimulateAnimationEvents = true;
        private bool _hitScheduled;
        private double _hitDueTime;

        public BrainTestHarness()
        {
            Body = new FakeBody(Tuning);
            Brain = new HeroCombatBrain<FakeTarget>(StateMachine, Body, Tuning);

            Brain.OnAttackWindupStarted += () =>
            {
                WindupStartTimes.Add(Brain.Clock);
                _hitScheduled = true;
                _hitDueTime = Brain.Clock + Tuning.WindupSeconds;
            };
            Brain.OnWindupWatchdogFired += () => WatchdogFires++;
            StateMachine.OnStateChanged += (from, to) => Transitions.Add(from + ">" + to);
            StateMachine.OnTransitionRejected += (from, to) => RejectedTransitions++;
        }

        public PlayerState State => StateMachine.CurrentState;

        public void Step()
        {
            Body.Tick(TickSeconds);
            Brain.Tick(TickSeconds);

            if (SimulateAnimationEvents && _hitScheduled && Brain.Clock >= _hitDueTime - 1e-9)
            {
                _hitScheduled = false;
                Brain.NotifyAttackHit();
            }
        }

        public void Advance(double seconds)
        {
            int steps = (int)System.Math.Round(seconds / TickSeconds);
            for (int i = 0; i < steps; i++) Step();
        }

        // 推進到指定狀態出現為止；逾時回傳 false。
        public bool AdvanceUntil(PlayerState state, double timeoutSeconds)
        {
            int steps = (int)System.Math.Round(timeoutSeconds / TickSeconds);
            for (int i = 0; i < steps; i++)
            {
                if (State == state) return true;
                Step();
            }
            return State == state;
        }
    }
}
