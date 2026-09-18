using NUnit.Framework;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests
{
    public sealed class HeroCombatBrainTests
    {
        private const double Tick = BrainTestHarness.TickSeconds;

        private static BrainTestHarness StartAttacking(out FakeTarget target)
        {
            BrainTestHarness h = new BrainTestHarness();
            target = new FakeTarget();
            h.Brain.CommandAttack(target);
            return h;
        }

        [Test] // A4
        public void MoveCommandDuringWindup_InterruptsImmediately_WithoutDamage()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget target);
            Assert.AreEqual(PlayerState.AttackWindup, h.State);
            h.Advance(0.10);
            Assert.AreEqual(PlayerState.AttackWindup, h.State);

            h.Brain.CommandMove(new GroundPoint(3f, 0f, 4f));

            Assert.AreEqual(PlayerState.Moving, h.State, "必須在同一呼叫內轉 Moving");
            Assert.AreEqual(1, h.Body.MoveToCount);
            Assert.AreEqual(3f, h.Body.LastMoveDestination.X);
            Assert.AreEqual(0, target.HitsTaken);

            // 被打斷的那一刀，其動畫事件若仍在混合期間觸發，不得造成傷害
            h.Brain.NotifyAttackHit();
            h.Advance(0.5);
            Assert.AreEqual(0, target.HitsTaken);
            Assert.AreEqual(0, h.RejectedTransitions);
        }

        [Test] // A5
        public void Hit_OpensWindow_AndFlickCancelsBackswingInSameCall()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget target);
            Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 0.4));

            Assert.AreEqual(1, target.HitsTaken);
            Assert.IsTrue(h.StateMachine.IsInCadenceWindow);
            Assert.AreEqual(0.25, h.Brain.Clock, 2 * Tick, "命中應發生在前搖 0.25s");

            h.Advance(0.20); // 窗口 0.22s 的尾端仍然有效
            Assert.AreEqual(PlayerState.AttackRelease, h.State);

            h.Brain.CommandFlick(0f, 1f);

            Assert.AreEqual(PlayerState.CadenceDashing, h.State, "0 幀：不得等到下一次 Tick");
            Assert.AreEqual(2, h.Body.Sim.Charges);
            Assert.AreEqual(1, h.Body.DashDistances.Count);
            Assert.AreEqual(1.4f, h.Body.DashDistances[0], 1e-4f);
            Assert.AreEqual(1, target.HitsTaken, "切後搖不得重複結算傷害");
        }

        [Test] // A6 正例
        public void FlickWithin120msBeforeHit_IsBufferedAndReleasedOnHitFrame()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget _);
            h.SimulateAnimationEvents = false;

            h.Advance(0.15);
            h.Brain.CommandFlick(1f, 0f);
            Assert.AreEqual(PlayerState.AttackWindup, h.State, "前搖中 flick 只入緩衝，不得提前滑");
            Assert.AreEqual(3, h.Body.Sim.Charges);

            h.Advance(0.10);
            h.Brain.NotifyAttackHit();

            Assert.AreEqual(PlayerState.CadenceDashing, h.State, "命中幀當下釋放緩衝");
            Assert.AreEqual(2, h.Body.Sim.Charges);
        }

        [Test] // A6 反例
        public void FlickOlderThan120msBeforeHit_IsDiscarded()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget target);
            h.SimulateAnimationEvents = false;

            h.Advance(0.10);
            h.Brain.CommandFlick(1f, 0f);
            h.Advance(0.15);
            h.Brain.NotifyAttackHit();

            Assert.AreEqual(PlayerState.AttackRelease, h.State);
            Assert.AreEqual(3, h.Body.Sim.Charges);
            Assert.AreEqual(1, target.HitsTaken);
        }

        [Test] // A7
        public void NoFlick_WindowExpiresInto150msRecovery_ThenLeaves()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget _);
            Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 0.4));

            h.Advance(0.21);
            Assert.AreEqual(PlayerState.AttackRelease, h.State);
            h.Advance(0.03);  // 命中後 0.24s
            Assert.AreEqual(PlayerState.AttackRecovery, h.State);
            h.Advance(0.12);  // 命中後 0.36s：收招 0.15s 尚未走完
            Assert.AreEqual(PlayerState.AttackRecovery, h.State);
            h.Advance(0.04);  // 命中後 0.40s
            Assert.AreNotEqual(PlayerState.AttackRecovery, h.State);
            Assert.AreEqual(PlayerState.Idle, h.State, "攻擊週期未到：原地等，不是硬直（見 A8 指令照收）");
        }

        [Test] // A8-1
        public void SpammingAttackCommand_KeepsConstantCadence_NoJam()
        {
            BrainTestHarness h = new BrainTestHarness();
            FakeTarget target = new FakeTarget();

            int ticks = (int)System.Math.Round(3.0 / Tick);
            for (int i = 0; i < ticks; i++)
            {
                if (i % 2 == 0) h.Brain.CommandAttack(target); // 每 ~16ms 狂點
                h.Step();
            }

            Assert.AreEqual(4, h.WindupStartTimes.Count, "3 秒、週期 0.8s：起手應為 t≈0, 0.8, 1.6, 2.4");
            AssertIntervals(h, 0.8, 0.8 + 2 * Tick);
            Assert.AreEqual(4, target.HitsTaken);
            Assert.AreEqual(0, h.RejectedTransitions);
            Assert.AreEqual(0, h.WatchdogFires);
        }

        [Test] // A8-2
        public void SpammingFlickWithZeroCharges_DoesNotDisturbAttackCadence()
        {
            BrainTestHarness h = new BrainTestHarness();
            h.Body.Sim.Charges = 0;
            h.Tuning.ChargeRecoverySeconds = 1000f; // 本測試期間不回充
            FakeTarget target = new FakeTarget();
            h.Brain.CommandAttack(target);

            int ticks = (int)System.Math.Round(3.0 / Tick);
            for (int i = 0; i < ticks; i++)
            {
                h.Brain.CommandFlick(1f, 0f);
                h.Step();
                Assert.AreNotEqual(PlayerState.CadenceDashing, h.State);
            }

            Assert.AreEqual(4, h.WindupStartTimes.Count);
            AssertIntervals(h, 0.8, 0.8 + 2 * Tick);
            Assert.AreEqual(0, h.Body.DashDistances.Count);
        }

        [Test] // A8-3：沒有任何狀態會拒收移動指令
        public void MoveCommand_IsHonouredFromEveryPhase1State()
        {
            PlayerState[] states =
            {
                PlayerState.Idle, PlayerState.Moving, PlayerState.AttackWindup,
                PlayerState.AttackRelease, PlayerState.CadenceDashing, PlayerState.AttackRecovery
            };

            for (int i = 0; i < states.Length; i++)
            {
                BrainTestHarness h = DriveInto(states[i]);
                Assert.AreEqual(states[i], h.State, "前置：無法進入 " + states[i]);

                int movesBefore = h.Body.MoveToCount;
                h.Brain.CommandMove(new GroundPoint(9f, 0f, 9f));
                h.Advance(0.30); // 最壞情況：收招 0.15s 或滑步 0.12s 走完

                Assert.AreEqual(PlayerState.Moving, h.State, "自 " + states[i] + " 下移動指令後未能步行");
                Assert.AreEqual(movesBefore + 1, h.Body.MoveToCount);
                Assert.AreEqual(9f, h.Body.LastMoveDestination.X);
                Assert.IsNull(h.Brain.CurrentTarget, "移動指令應解除攻擊鎖定");
            }
        }

        [Test] // A9
        public void DashCancellingEveryHit_NeverShortensAttackPeriod()
        {
            BrainTestHarness h = new BrainTestHarness();
            // 關閉回充讓情境具決定性：否則第一格充能會在 t≈2.75 回復，恰好落在第 4 刀的目押窗口 (2.65~2.87) 內而合法多滑一次
            h.Tuning.ChargeRecoverySeconds = 1000f;
            FakeTarget target = new FakeTarget();
            h.Brain.CommandAttack(target);

            int ticks = (int)System.Math.Round(3.0 / Tick);
            for (int i = 0; i < ticks; i++)
            {
                h.Brain.CommandFlick(0f, -1f); // 等同模式 B 微輪盤一直推著
                h.Step();
            }

            Assert.AreEqual(3, h.Body.DashDistances.Count, "3 格充能 → 前三刀都有切後搖");
            Assert.AreEqual(1.4f, h.Body.DashDistances[0], 1e-4f);
            Assert.AreEqual(0.9f, h.Body.DashDistances[1], 1e-4f);
            Assert.AreEqual(0.5f, h.Body.DashDistances[2], 1e-4f);

            Assert.AreEqual(4, h.WindupStartTimes.Count, "3 秒、週期 0.8s：t≈0, 0.8, 1.6, 2.4");
            AssertIntervals(h, 0.8, 0.8 + 2 * Tick);
        }

        [Test] // A10
        public void FlickOutsideCadenceWindow_IsIgnored()
        {
            PlayerState[] states =
            {
                PlayerState.Idle, PlayerState.Moving, PlayerState.AttackRecovery, PlayerState.CadenceDashing
            };

            for (int i = 0; i < states.Length; i++)
            {
                BrainTestHarness h = DriveInto(states[i]);
                Assert.AreEqual(states[i], h.State, "前置：無法進入 " + states[i]);

                int charges = h.Body.Sim.Charges;
                int dashes = h.Body.DashDistances.Count;
                h.Brain.CommandFlick(1f, 1f);

                Assert.AreEqual(states[i], h.State, "flick 不得改變狀態：" + states[i]);
                Assert.AreEqual(charges, h.Body.Sim.Charges);
                Assert.AreEqual(dashes, h.Body.DashDistances.Count);
            }
        }

        [Test]
        public void MoveTapDuringWindow_ClosesItEarly_RecoversThenWalks()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget _);
            Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 0.4));
            h.Advance(0.05);

            h.Brain.CommandMove(new GroundPoint(1f, 0f, 1f));
            Assert.AreEqual(PlayerState.AttackRecovery, h.State);

            h.Brain.CommandFlick(1f, 0f); // 窗口已關
            Assert.AreEqual(PlayerState.AttackRecovery, h.State);

            h.Advance(0.10);
            Assert.AreEqual(PlayerState.AttackRecovery, h.State);
            h.Advance(0.07);
            Assert.AreEqual(PlayerState.Moving, h.State);
        }

        [Test]
        public void QueuedAttackOnNewTarget_TakesOverAfterRecovery()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget first);
            FakeTarget second = new FakeTarget();
            Assert.IsTrue(h.AdvanceUntil(PlayerState.AttackRelease, 0.4));

            h.Brain.CommandAttack(second);
            h.Advance(1.0);

            Assert.AreSame(second, h.Brain.CurrentTarget);
            Assert.AreEqual(1, first.HitsTaken);
            Assert.AreEqual(1, second.HitsTaken);
        }

        [Test] // 紅線 1 的破口：射程內交替狂點兩個目標，不得讓前搖無限重置、永遠打不出去
        public void AlternatingBetweenTwoTargets_DoesNotStallTheAttack()
        {
            BrainTestHarness h = new BrainTestHarness();
            FakeTarget left = new FakeTarget();
            FakeTarget right = new FakeTarget();

            int ticks = (int)System.Math.Round(3.0 / Tick);
            for (int i = 0; i < ticks; i++)
            {
                if (i % 6 == 0) h.Brain.CommandAttack((i / 6) % 2 == 0 ? left : right); // 每 50ms 換一個目標點
                h.Step();
            }

            Assert.AreEqual(4, left.HitsTaken + right.HitsTaken, "3 秒、週期 0.8s 應出 4 刀，改鎖不得吃掉出刀節奏");
            Assert.AreEqual(4, h.WindupStartTimes.Count, "改鎖不得重新起手");
            AssertIntervals(h, 0.8, 0.8 + 2 * Tick);
            Assert.AreEqual(0, h.RejectedTransitions);
        }

        [Test]
        public void RetargetDuringWindup_HitsTheNewTarget_KeepingWindupProgress()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget first);
            FakeTarget second = new FakeTarget();
            h.Advance(0.15);

            h.Brain.CommandAttack(second);
            Assert.AreEqual(PlayerState.AttackWindup, h.State);

            h.Advance(0.12); // 原前搖 0.25s 到點
            Assert.AreEqual(0, first.HitsTaken);
            Assert.AreEqual(1, second.HitsTaken, "命中的是改鎖後的目標，且沒有因改鎖而延後");
        }

        [Test]
        public void RetargetDuringWindup_ToOutOfRangeTarget_BreaksOffAndChases()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget first);
            FakeTarget far = new FakeTarget { InRange = false };
            h.Advance(0.1);

            h.Brain.CommandAttack(far);

            Assert.AreEqual(PlayerState.Moving, h.State);
            h.Advance(0.4);
            Assert.AreEqual(0, first.HitsTaken + far.HitsTaken, "被打斷的前搖不得出傷害");
        }

        [Test]
        public void OutOfRangeTarget_IsChased_ThenAttackedOnceInRange()
        {
            BrainTestHarness h = new BrainTestHarness();
            FakeTarget target = new FakeTarget { InRange = false };

            h.Brain.CommandAttack(target);
            Assert.AreEqual(PlayerState.Moving, h.State);
            Assert.AreEqual(1, h.Body.ChaseCount);

            h.Advance(0.2);
            Assert.AreEqual(0, h.WindupStartTimes.Count);

            target.InRange = true;
            h.Advance(0.05);
            Assert.AreEqual(PlayerState.AttackWindup, h.State);
        }

        [Test]
        public void PlainMove_ReturnsToIdle_OnArrival()
        {
            BrainTestHarness h = new BrainTestHarness();
            h.Brain.CommandMove(new GroundPoint(2f, 0f, 2f));
            h.Advance(0.5);
            Assert.AreEqual(PlayerState.Moving, h.State);

            h.Body.Arrived = true;
            h.Advance(0.05);
            Assert.AreEqual(PlayerState.Idle, h.State);
        }

        [Test]
        public void TargetDyingDuringWindup_AbortsWithoutDamage()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget target);
            h.Advance(0.1);
            target.Alive = false;
            h.Advance(0.3);

            Assert.AreEqual(PlayerState.Idle, h.State);
            Assert.AreEqual(0, target.HitsTaken);
            Assert.IsNull(h.Brain.CurrentTarget);
        }

        [Test]
        public void MissingAnimationEvent_WatchdogResolvesHit_InsteadOfSoftLock()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget target);
            h.SimulateAnimationEvents = false;

            h.Advance(0.55);
            Assert.AreEqual(PlayerState.AttackWindup, h.State);
            Assert.AreEqual(0, h.WatchdogFires);

            h.Advance(0.10);
            Assert.AreEqual(1, h.WatchdogFires);
            Assert.AreEqual(1, target.HitsTaken);
        }

        [Test]
        public void HitEventArrivingTooEarlyInWindup_IsTreatedAsStale()
        {
            BrainTestHarness h = StartAttacking(out FakeTarget target);
            h.SimulateAnimationEvents = false;

            h.Advance(0.05);
            h.Brain.NotifyAttackHit();

            Assert.AreEqual(PlayerState.AttackWindup, h.State);
            Assert.AreEqual(0, target.HitsTaken);
        }

        // ───────────────────────── 工具 ─────────────────────────

        private static void AssertIntervals(BrainTestHarness h, double minInclusive, double maxInclusive)
        {
            for (int i = 1; i < h.WindupStartTimes.Count; i++)
            {
                double interval = h.WindupStartTimes[i] - h.WindupStartTimes[i - 1];
                Assert.IsTrue(interval >= minInclusive - 1e-6, "第 " + i + " 刀起手間隔 " + interval + " 短於攻擊週期");
                Assert.IsTrue(interval <= maxInclusive + 1e-6, "第 " + i + " 刀起手間隔 " + interval + " 過長（疑似卡刀）");
            }
        }

        private static BrainTestHarness DriveInto(PlayerState state)
        {
            BrainTestHarness h = new BrainTestHarness();
            FakeTarget target = new FakeTarget();

            switch (state)
            {
                case PlayerState.Idle:
                    break;

                case PlayerState.Moving:
                    h.Brain.CommandMove(new GroundPoint(5f, 0f, 5f));
                    break;

                case PlayerState.AttackWindup:
                    h.Brain.CommandAttack(target);
                    h.Advance(0.1);
                    break;

                case PlayerState.AttackRelease:
                    h.Brain.CommandAttack(target);
                    h.AdvanceUntil(PlayerState.AttackRelease, 0.4);
                    break;

                case PlayerState.CadenceDashing:
                    h.Brain.CommandAttack(target);
                    h.AdvanceUntil(PlayerState.AttackRelease, 0.4);
                    h.Brain.CommandFlick(1f, 0f);
                    break;

                case PlayerState.AttackRecovery:
                    h.Brain.CommandAttack(target);
                    h.AdvanceUntil(PlayerState.AttackRelease, 0.4);
                    h.AdvanceUntil(PlayerState.AttackRecovery, 0.4);
                    break;
            }
            return h;
        }
    }
}
