using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests
{
    public sealed class PlayerStateMachineTests
    {
        [Test] // A11
        public void IllegalTransitions_AreRejected_StateAndEventsUntouched()
        {
            PlayerStateMachine sm = new PlayerStateMachine();
            int changed = 0, rejected = 0;
            sm.OnStateChanged += (a, b) => changed++;
            sm.OnTransitionRejected += (a, b) => rejected++;

            sm.ChangeState(PlayerState.CadenceDashing);  // 對空滑步
            sm.ChangeState(PlayerState.AttackRelease);   // 沒前搖就出傷害
            sm.ChangeState(PlayerState.AttackRecovery);

            Assert.AreEqual(PlayerState.Idle, sm.CurrentState);
            Assert.AreEqual(0, changed);
            Assert.AreEqual(3, rejected);
        }

        [Test]
        public void CadenceDash_IsOnlyReachableFromAttackRelease()
        {
            for (int from = 0; from < 7; from++)
            {
                bool allowed = PlayerStateMachine.IsTransitionAllowed((PlayerState)from, PlayerState.CadenceDashing);
                Assert.AreEqual((PlayerState)from == PlayerState.AttackRelease, allowed, ((PlayerState)from).ToString());
            }
        }

        [Test]
        public void AttackRelease_HasExactlyTwoExits_DashOrRecovery()
        {
            for (int to = 0; to < 7; to++)
            {
                PlayerState target = (PlayerState)to;
                bool expected = target == PlayerState.CadenceDashing || target == PlayerState.AttackRecovery;
                Assert.AreEqual(expected, PlayerStateMachine.IsTransitionAllowed(PlayerState.AttackRelease, target), target.ToString());
            }
        }

        [Test]
        public void LegalAttackCycle_RaisesEventsWithOldAndNewState()
        {
            PlayerStateMachine sm = new PlayerStateMachine();
            string log = "";
            sm.OnStateChanged += (a, b) => log += a + ">" + b + ";";

            sm.ChangeState(PlayerState.AttackWindup);
            Assert.IsFalse(sm.IsInCadenceWindow);
            sm.ChangeState(PlayerState.AttackRelease);
            Assert.IsTrue(sm.IsInCadenceWindow);
            Assert.IsFalse(sm.CanMove);
            sm.ChangeState(PlayerState.CadenceDashing);
            Assert.IsFalse(sm.IsInCadenceWindow);
            sm.ChangeState(PlayerState.Idle);
            Assert.IsTrue(sm.CanMove && sm.CanAttack);

            Assert.AreEqual("Idle>AttackWindup;AttackWindup>AttackRelease;AttackRelease>CadenceDashing;CadenceDashing>Idle;", log);
        }

        [Test]
        public void ChangingToSameState_IsSilentNoOp()
        {
            PlayerStateMachine sm = new PlayerStateMachine();
            int changed = 0, rejected = 0;
            sm.OnStateChanged += (a, b) => changed++;
            sm.OnTransitionRejected += (a, b) => rejected++;

            sm.ChangeState(PlayerState.Idle);

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, rejected);
        }
    }
}
