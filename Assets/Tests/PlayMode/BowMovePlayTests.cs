#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    public sealed partial class CameraLabCombatPlayTests
    {
        private Vector2 _bowMoveStick;
        private const float BowMoveNormalSpeed = 5.5f;

        private void BeginBowMove(Vector2 direction)
        {
            _bowMoveStick = new Vector2(Screen.width * .15f, Screen.height * .2f);
            _input.BeginSimulatedHold(1, _bowMoveStick.x, _bowMoveStick.y);
            ChangeBowMove(direction);
        }

        private void ChangeBowMove(Vector2 direction)
        {
            Vector2 position = _bowMoveStick + direction * _input.ContinuousRouter.JoystickRadiusPixels;
            _input.MoveSimulatedHold(1, position.x, position.y);
        }

        private void AssertBowStep(Vector3 before, Vector3 direction)
        {
            float distance = Vector3.Dot(Flat(_hero.transform.position - before), direction);
            Assert.GreaterOrEqual(distance, BowMoveNormalSpeed * Time.deltaTime * .95f,
                "弓每幀沿搖桿方向正常步速移動，不可因前搖或後搖停步/減速；before=" + before
                + " after=" + _hero.transform.position + " dummy=" + _bowDummy.transform.position
                + " state=" + _hero.StateMachine.CurrentState);
            Assert.LessOrEqual(distance, BowMoveNormalSpeed * Time.deltaTime * 1.05f,
                "弓不得疊加導航位移超過正常步速");
        }

        private void AssertBowPathClear(Vector3 direction)
        {
            foreach (RaycastHit hit in Physics.SphereCastAll(_hero.transform.position + Vector3.up * .9f,
                .4f, direction, .3f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.root == _hero.transform.root || hit.normal.y > .7f) continue;
                if (hit.distance <= 0f && hit.point == Vector3.zero) continue;
                Assert.Fail("正常步速fixture路徑被障礙擋住：" + hit.collider.name);
            }
        }

        private IEnumerator BowMoveUntilHit(Vector3 direction, float health, float damage, int cues)
        {
            float end = Time.time + 1.5f;
            while (_bowDummy.Health == health && Time.time < end)
            {
                Vector3 before = _hero.transform.position;
                yield return null;
                AssertBowStep(before, direction);
            }
            Assert.AreEqual(health - damage, _bowDummy.Health, .001f, "移動中第一發保留原傷害");
            Assert.AreEqual(cues + 1, _lab.BowReleaseCueCount, "第一發傷害與一支箭一一對應");
            Assert.AreEqual(true, _lab.LastArrowVisual.Hit, "真命中箭已生成");
            Debug.Log("[BOWMOVE] damage=" + damage + " releaseCues=" + (_lab.BowReleaseCueCount - cues));
        }

        private IEnumerator BowMoveThroughRecovery(Vector3 direction)
        {
            float recoverBy = Time.time + 1f;
            while (_hero.StateMachine.CurrentState == PlayerState.AttackRelease
                || _hero.StateMachine.CurrentState == PlayerState.AttackRecovery)
            {
                Assert.Less(Time.time, recoverBy, "收招不能無限延長");
                Vector3 before = _hero.transform.position;
                yield return null;
                AssertBowStep(before, direction);
            }
        }

        [UnityTest]
        public IEnumerator M1_Bow_MoveBeforeQuickAndChargedRelease_KeepsNormalSpeedAndDamage()
        {
            foreach (bool charged in new[] { false, true })
            {
                yield return SetupBow(10f);
                BeginBowMove(Vector2.up);
                yield return null;
                PressAttack();
                double start = Time.unscaledTimeAsDouble;
                int holdFrame = 0;
                Vector3 shotMove = Vector3.right;
                while (Time.unscaledTimeAsDouble - start < (charged ? 1.02 : .08))
                {
                    // captureDeltaTime 與觸控 unscaled 時鐘分離：小範圍折返，避免真按住期間走進木樁。
                    Vector2 stickDirection = holdFrame++ % 6 < 3 ? Vector2.right : Vector2.left;
                    shotMove = new Vector3(stickDirection.x, 0f, 0f);
                    ChangeBowMove(stickDirection);
                    AssertBowPathClear(shotMove);
                    Vector3 before = _hero.transform.position;
                    yield return null;
                    Assert.Greater(FlatDistance(_hero.transform.position, _bowDummy.transform.position), 2f,
                        "位移量測區域必須與木樁保持碰撞安全距離");
                    AssertBowStep(before, shotMove);
                    Assert.AreEqual(1000f, _bowDummy.Health, "按住蓄力時不自行出箭");
                }
                int cues = _lab.BowReleaseCueCount;
                ReleaseAttack();
                Assert.AreSame(_bowDummy, _lab.LastAimTarget, "原輔助瞄準仍選正前方目標");
                yield return BowMoveUntilHit(shotMove, 1000f, charged ? 108f : 60f, cues);
                // 前搖之後的出手窗口與收招也不鎖移動。
                yield return BowMoveThroughRecovery(shotMove);
                _input.EndSimulatedHold(1);
            }
        }

        [UnityTest]
        public IEnumerator M2_Bow_StartAndReverseJoystickDuringWindup_DoesNotCancelShot()
        {
            foreach (bool charged in new[] { false, true })
            {
                yield return SetupBow(10f);
                yield return HoldAttack(charged ? 1.02 : .08);
                Assert.AreEqual(PlayerState.AttackWindup, _hero.StateMachine.CurrentState);
                int cues = _lab.BowReleaseCueCount;
                BeginBowMove(Vector2.right);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 before = _hero.transform.position;
                    yield return null;
                    AssertBowStep(before, Vector3.right);
                }
                Assert.AreEqual(1000f, _bowDummy.Health, "換向仍發生在尚未命中的前搖");
                ChangeBowMove(Vector2.left);
                yield return BowMoveUntilHit(Vector3.left, 1000f, charged ? 108f : 60f, cues);
                yield return BowMoveThroughRecovery(Vector3.left);
                _input.EndSimulatedHold(1);
            }
        }

        [UnityTest]
        public IEnumerator M3_Bow_ReleasingJoystickStopsWalkingButKeepsPendingShot()
        {
            yield return SetupBow(10f);
            BeginBowMove(Vector2.up);
            yield return null;
            yield return HoldAttack(1.02);
            int cues = _lab.BowReleaseCueCount;
            yield return null;
            _input.EndSimulatedHold(1);
            Vector3 stopped = _hero.transform.position;
            yield return WaitSeconds(.2f);
            Assert.LessOrEqual(FlatDistance(stopped, _hero.transform.position), .05f, "鬆開搖桿即停步");
            yield return WaitForDrop(_bowDummy, 1000f, 1.5f);
            Assert.AreEqual(892f, _bowDummy.Health, .001f, "鬆搖桿不取消蓄力箭");
            Assert.AreEqual(cues + 1, _lab.BowReleaseCueCount);
        }

        [UnityTest]
        public IEnumerator M4_Bow_EmptyShotWhileMoving_GeneratesArrowWithoutDamage()
        {
            yield return SetupBow(14f);
            BeginBowMove(Vector2.right);
            yield return null;
            int cues = _lab.BowReleaseCueCount;
            Vector3 before = _hero.transform.position;
            TapAttack();
            yield return null;
            AssertBowStep(before, Vector3.right);
            Assert.AreEqual(cues + 1, _lab.BowReleaseCueCount);
            Assert.IsFalse(_lab.LastArrowVisual.Hit);
            Assert.AreEqual(1000f, _bowDummy.Health);
        }

        [UnityTest]
        public IEnumerator M5_Bow_MovingShotLifecycle_ClearsChargedStateAndArrow()
        {
            for (int kind = 0; kind < 4; kind++)
            {
                yield return SetupBow(10f);
                BeginBowMove(Vector2.up);
                yield return null;
                yield return HoldAttack(1.02);
                Assert.AreEqual(PlayerState.AttackWindup, _hero.StateMachine.CurrentState);
                int cues = _lab.BowReleaseCueCount;
                if (kind == 0) TapWeapon();
                else if (kind == 1) _lab.SetThirdPerson(false);
                else if (kind == 2) _lab.enabled = false;
                else _hero.TakeDuelDamage(10000f, DamageType.True);
                yield return WaitSeconds(.5f);
                Assert.IsFalse(_hero.HasChargedShot, "生命週期清掉已放開的蓄力箭，kind=" + kind);
                Assert.AreEqual(kind == 1 || kind == 2 ? 940f : 1000f, _bowDummy.Health, .001f,
                    "TOP/停用只保留原普通60傷，換錘/死亡取消；不可把108結算後清旗標誤當已解除蓄力，kind=" + kind);
                Assert.AreEqual(cues, _lab.BowReleaseCueCount, "切換/倒地後不得冒出弓箭，kind=" + kind);
                Assert.IsTrue(_lab.LastArrowObject == null || !_lab.LastArrowObject.activeInHierarchy);
                _input.EndSimulatedHold(1);
            }
        }

        [UnityTest]
        public IEnumerator M3b_Bow_ReleaseJoystickAfterLongRangeHit_DoesNotResumeNavigation()
        {
            yield return SetupBow(14f);
            yield return HoldAttack(1.02);
            BeginBowMove(Vector2.right);
            yield return BowMoveUntilHit(Vector3.right, 1000f, 108f, 0);
            float until = Time.time + 1f;
            while (_hero.StateMachine.CurrentState != PlayerState.AttackRecovery && Time.time < until) yield return null;
            Assert.AreEqual(PlayerState.AttackRecovery, _hero.StateMachine.CurrentState);
            _input.EndSimulatedHold(1);
            Vector3 stopped = _hero.transform.position;
            yield return WaitSeconds(.4f);
            float travel = FlatDistance(stopped, _hero.transform.position);
            Debug.Log("[BOWMOVE M3b] afterReleaseTravel=" + travel + " state=" + _hero.StateMachine.CurrentState);
            Assert.LessOrEqual(travel, .05f, "鬆桿不能在蓄力射程回復12m後重新自動追擊");
            Vector3 targetDirection = _bowDummy.transform.position - _hero.transform.position;
            _lab.RotateThirdPerson(Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg - _lab.YawDegrees);
            yield return WorldTapDummy(_bowDummy);
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "新的明確目標指令可重新追擊");
            Vector3 resumed = _hero.transform.position;
            yield return WaitForDrop(_bowDummy, 892f, 2f);
            Assert.Greater(FlatDistance(resumed, _hero.transform.position), .5f, "新指令需真的追到普通射程");
            Assert.AreEqual(832f, _bowDummy.Health, .001f, "新指令第一下回到60，未殘留滿蓄");
        }

        [UnityTest]
        public IEnumerator M3d_Bow_ReleasedJoystickThenTargetConcealed_DoesNotChaseLastSeen()
        {
            yield return StartCanyonMatchThirdPerson();
            yield return PlaceCanyon(TileCenter(0) + new Vector3(0f, 0f, -1.5f),
                TileCenter(0) + new Vector3(0f, 0f, 1.5f), true);
            SelectWeaponByTaps(2);
            yield return null;
            Assert.IsTrue(_hero.CanEngage(_red));
            float health = _red.HealthNormalized;
            BeginBowMove(Vector2.right);
            yield return null;
            TapAttack();
            int guard = 0;
            while (_hero.StateMachine.CurrentState != PlayerState.AttackRelease && guard++ < 120)
                yield return null;
            Assert.AreEqual(PlayerState.AttackRelease, _hero.StateMachine.CurrentState);
            Assert.Less(_red.HealthNormalized, health, "原攻擊確已命中");
            _input.EndSimulatedHold(1);
            Vector3 stopped = _hero.transform.position;
            _red.GetComponent<HeroLocomotion>().WarpTo(TileCenter(5));
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_red), "谷底看不到移上崖台的目標");
            yield return WaitSeconds(.6f);
            float travel = FlatDistance(stopped, _hero.transform.position);
            Debug.Log("[BOWMOVE M3d] concealedAfterReleaseTravel=" + travel);
            Assert.LessOrEqual(travel, .05f, "鬆桿後不得因目標失去視野而追向最後看見的位置");
            Assert.IsNull(_hero.LostTargetForTest);
        }

        [UnityTest]
        public IEnumerator M6_Bow_MovingRepeatedQuickShots_PreserveAttackPeriod()
        {
            yield return SetupBow(10f);
            BeginBowMove(Vector2.right);
            yield return null;
            TapAttack();
            float health = _bowDummy.Health;
            float lastHit = -10f;
            int hits = 0;
            float end = Time.time + 2.4f;
            int frame = 0;
            while (Time.time < end)
            {
                if (frame % 15 == 0) TapAttack();
                // 小區域折返以保持目標在正常射程與瞄準錐内。
                ChangeBowMove(frame++ % 6 < 3 ? Vector2.right : Vector2.left);
                yield return null;
                if (_bowDummy.Health < health)
                {
                    Assert.AreEqual(60f, health - _bowDummy.Health, .001f);
                    if (hits > 0) Assert.GreaterOrEqual(Time.time - lastHit, .8f - Time.deltaTime * 1.1f,
                        "移動/換向/連點不得重置冷卻以加快普攻");
                    hits++;
                    lastHit = Time.time;
                    health = _bowDummy.Health;
                }
            }
            Assert.GreaterOrEqual(hits, 3, "持續移動連點也應持續命中");
            Assert.AreEqual(hits, _lab.BowReleaseCueCount, "箭數與真傷害次數相符");
        }

        [UnityTest]
        public IEnumerator M3c_Bow_QueuedQuickShotThenJoystickRelease_KeepsNewAttackIntent()
        {
            yield return SetupBow(6f);
            BeginBowMove(Vector2.right);
            yield return null;
            TapAttack();
            yield return BowMoveUntilHit(Vector3.right, 1000f, 60f, 0);
            Assert.AreEqual(PlayerState.AttackRelease, _hero.StateMachine.CurrentState);
            var queued = SpawnDummyAt(_bowDummy, 45f, 11.9f);
            queued.Configure(1000f, queued.TargetFaction);
            _lab.RotateThirdPerson(45f);
            yield return null;
            ChangeBowMove(Vector2.down);
            TapAttack();
            Assert.AreSame(queued, _lab.LastAimTarget, "真ATK快速射擊在前一發後搖中排入新目標");
            yield return null; yield return null;
            Assert.Greater(FlatDistance(_hero.transform.position, queued.transform.position), 12f,
                "新箭排隊時因退步離開普通射程，需短追才能完成");
            _input.EndSimulatedHold(1);
            yield return WaitForDrop(queued, 1000f, 2f);
            Assert.AreEqual(940f, queued.Health, .001f, "鬆桿不能把未結算的新明確射擊誤判成舊自動追擊");
            Assert.AreEqual(2, _lab.BowReleaseCueCount, "兩次真傷害各有一支箭");
        }
    }
}
#endif
