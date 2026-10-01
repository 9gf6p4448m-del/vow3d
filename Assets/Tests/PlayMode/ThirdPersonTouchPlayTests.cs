#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    public sealed class ThirdPersonTouchPlayTests
    {
        private CameraComparisonLab _lab;
        private PlayerInputService _input;
        private HeroController _hero;
        private Phase1Bootstrap _bootstrap;
        private TrainingOpponent _opponent;
        private Vector2 _stick;
        private IEnumerator Load(bool startDuel = false, bool near = false)
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null; yield return null;
            _lab = Object.FindObjectOfType<CameraComparisonLab>();
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _input = Object.FindObjectOfType<PlayerInputService>();
            _hero = _lab.FollowedHero; _opponent = _bootstrap.Opponent;
            if (startDuel)
            {
                _hero.GetComponent<HeroLocomotion>().WarpTo(_opponent.transform.position + Vector3.back * (near ? 1.4f : _hero.AttackRange + 2f) + Vector3.left * .3f);
                yield return null;
                Vector3 p = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
                _input.SendScreenTap(p.x, p.y);
                Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
                _opponent.enabled = false; // 隔離玩家輸入；不改敵人戰鬥參數。
            }
            _lab.SetThirdPerson(true);
            yield return null;
            _stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
        }
        [TearDown]
        public void Cleanup()
        {
            if (_input != null) { _input.EndSimulatedHold(0); _input.EndSimulatedHold(1); }
            if (_lab != null) _lab.enabled = false;
            Time.captureDeltaTime = 0f;
        }
        private void Push(float x, float y)
        {
            _input.BeginSimulatedHold(0, _stick.x, _stick.y);
            _input.MoveSimulatedHold(0, _stick.x + x, _stick.y + y);
        }
        private void TapEnemy()
        {
            Vector3 p = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
            Assert.Greater(p.z, 0f); Assert.GreaterOrEqual(p.x, Screen.width * .5f, "敵人須在右側tap區");
            _input.SendScreenTap(p.x, p.y);
            Assert.AreSame(_opponent, _hero.CurrentTarget);
        }
        [UnityTest]
        public IEnumerator CameraRelativeMove_ActualDisplacement_ReleaseStops_AndLookDoesNotTurnIdleHero()
        {
            yield return Load();
            _lab.RotateThirdPerson(90f);
            Vector3 start = _hero.transform.position;
            Push(0, 50);
            for (int i = 0; i < 6; i++) yield return null;
            Vector3 delta = _hero.transform.position - start;
            Assert.Greater(delta.x, .1f); Assert.Less(Mathf.Abs(delta.z), .03f);
            _input.EndSimulatedHold(0);
            Vector3 stopped = _hero.transform.position;
            for (int i = 0; i < 4; i++) yield return null;
            Assert.Less(Vector3.Distance(stopped, _hero.transform.position), .001f);
            Quaternion facing = _hero.transform.rotation;
            _input.BeginSimulatedHold(1, Screen.width * .65f, Screen.height * .5f);
            _input.MoveSimulatedHold(1, Screen.width * .8f, Screen.height * .6f);
            yield return null; yield return null;
            Assert.Greater(_lab.YawDegrees, 90f);
            Assert.Less(Quaternion.Angle(facing, _hero.transform.rotation), .001f);
        }
        [UnityTest]
        public IEnumerator TwoRealRouterHolds_MoveAndLookTogether_ClampPitch_AndDoNotEmitWorldCommands()
        {
            yield return Load();
            int moves = 0, targets = 0, flicks = 0;
            _input.OnMoveDestinationSelected += _ => moves++;
            _input.OnCombatTargetSelected += _ => targets++;
            _input.OnCadenceVectorFlicked += _ => flicks++;
            Vector3 start = _hero.transform.position;
            Push(0, 40);
            _input.BeginSimulatedHold(1, Screen.width * .65f, Screen.height * .5f);
            _input.MoveSimulatedHold(1, Screen.width * .8f, Screen.height * 4f);
            yield return null; yield return null;
            Assert.Greater(Vector3.Distance(start, _hero.transform.position), .03f);
            Assert.AreEqual(10f, _lab.PitchDegrees);
            Assert.Greater(_lab.YawDegrees, 0f);
            _input.MoveSimulatedHold(1, Screen.width * .8f, -Screen.height * 4f);
            yield return null; yield return null;
            Assert.AreEqual(50f, _lab.PitchDegrees);
            _input.EndSimulatedHold(0); _input.EndSimulatedHold(1);
            Assert.AreEqual(0, moves); Assert.AreEqual(0, targets); Assert.AreEqual(0, flicks);
        }
        [UnityTest]
        public IEnumerator RightGroundTapDoesNotNavigate_EnemyTapUsesExistingAttack_AndNewStickIntentCancelsWindup()
        {
            yield return Load(true, true);
            int moves = 0;
            _input.OnMoveDestinationSelected += _ => moves++;
            _input.SendScreenTap(Screen.width * .65f, Screen.height * .3f);
            Assert.AreEqual(0, moves);
            Push(10, 0); yield return null;
            TapEnemy();
            Assert.AreEqual(PlayerState.AttackWindup, _hero.StateMachine.CurrentState);
            yield return null;
            Assert.AreEqual(PlayerState.AttackWindup, _hero.StateMachine.CurrentState, "固定推桿不該每幀打斷攻擊");
            Vector3 start = _hero.transform.position;
            _input.MoveSimulatedHold(0, _stick.x - 40f, _stick.y);
            yield return null;
            Assert.IsNull(_hero.CurrentTarget, "新的真stick操作應按既有移動命令清目標");
            Assert.Greater(Vector3.Distance(start, _hero.transform.position), .02f);
        }
        [UnityTest]
        public IEnumerator TargetTapTakesOwnership_ReleaseDoesNotDestroyChase_ThenNewStickTakesItBack()
        {
            yield return Load(true);
            Push(10, 0); yield return null;
            Vector3 offset = _opponent.transform.position - _hero.transform.position;
            offset.y = 0f;
            Debug.Log("TouchOwnershipPrecondition distance=" + offset.magnitude + " attackRange=" + _hero.AttackRange
                + " inRange=" + _hero.IsTargetInAttackRange(_opponent));
            Assert.Greater(offset.magnitude, _hero.AttackRange, "遠敵fixture必須在原普攻射程外");
            Assert.IsFalse(_hero.IsTargetInAttackRange(_opponent), "點擊前需由正式判定確認真的走追擊分支");
            TapEnemy();
            Assert.AreEqual(PlayerState.Moving, _hero.StateMachine.CurrentState);
            _input.EndSimulatedHold(0);
            float distance = Vector3.Distance(_hero.transform.position, _opponent.transform.position);
            for (int i = 0; i < 8; i++) yield return null;
            Assert.AreSame(_opponent, _hero.CurrentTarget);
            Assert.Less(Vector3.Distance(_hero.transform.position, _opponent.transform.position), distance - .1f,
                "目標接手後放搖桿不可清掉追擊路徑而卡住");
            Push(-40, 0); yield return null;
            Assert.IsNull(_hero.CurrentTarget);
            _input.EndSimulatedHold(0);
            Vector3 stop = _hero.transform.position;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Less(Vector3.Distance(stop, _hero.transform.position), .001f);
        }
        [UnityTest]
        public IEnumerator RuneAndLabUiUseExistingPriority_WithoutWorldOrFlickLeak()
        {
            yield return Load();
            int moves = 0, targets = 0, flicks = 0, runeQuick = 0;
            _input.OnMoveDestinationSelected += _ => moves++;
            _input.OnCombatTargetSelected += _ => targets++;
            _input.OnCadenceVectorFlicked += _ => flicks++;
            _input.OnRuneQuickCastTriggered += () => runeQuick++;
            Assert.IsTrue(_lab.TryGetButtonScreenPoint(1, out Vector2 button));
            _input.SendScreenTap(button.x, button.y);
            Assert.AreEqual(330f, _lab.YawDegrees);
            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter);
            float runeX = (layout.Button.XMin + layout.Button.XMax) * .5f;
            float runeY = (layout.Button.YMin + layout.Button.YMax) * .5f;
            _input.SendScreenTap(runeX, runeY);
            Assert.AreEqual(1, runeQuick);
            RuneCaster caster = Object.FindObjectOfType<RuneCaster>();
            bool wallAlive = false;
            foreach (RuneWall wall in caster.Pool) wallAlive |= wall.IsAlive;
            Assert.IsTrue(wallAlive, "符印tap必須真正生出牆，不能只看sink事件");
            float scale = DebugHudLayout.Compute(Screen.width, Screen.height, Screen.dpi, true, true, true).Scale;
            _input.SendScreenTap(Screen.width - 282f * scale, 28f * scale);
            Assert.AreEqual(1, _bootstrap.ElementField.CountZonesOfKind(ElementZoneKind.Water), "第三人稱Water實際技能必須可施放");
            Push(30, 0);
            _input.MoveSimulatedHold(0, runeX, runeY);
            _input.EndSimulatedHold(0);
            Assert.AreEqual(1, runeQuick, "移動指跨符印區不可偷路由");
            Assert.AreEqual(0, moves); Assert.AreEqual(0, targets); Assert.AreEqual(0, flicks);
        }
        [UnityTest]
        public IEnumerator ResizeWithHeldMoveAndLook_InvalidatesBothAtFirstUpdate_ThenFreshTouchesWork()
        {
            yield return Load();
            using (var size = new GameViewSize(844, 390))
            {
                yield return null; yield return null;
                _stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
                Push(0, 40);
                _input.BeginSimulatedHold(1, Screen.width * .65f, Screen.height * .5f);
                _input.MoveSimulatedHold(1, Screen.width * .8f, Screen.height * .5f);
                yield return null; yield return null;
                using (var compact = new GameViewSize(640, 360))
                {
                    yield return null;
                    Assert.AreEqual(640, Screen.width);
                    Assert.AreEqual(0f, _input.ContinuousRouter.MoveY);
                    Assert.AreEqual(0f, _input.ContinuousRouter.LookDeltaX);
                    Vector3 stopped = _hero.transform.position;
                    float yaw = _lab.YawDegrees;
                    yield return null; yield return null;
                    Assert.Less(Vector3.Distance(stopped, _hero.transform.position), .001f);
                    Assert.AreEqual(yaw, _lab.YawDegrees);
                    _input.EndSimulatedHold(0); _input.EndSimulatedHold(1);
                    _stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
                    Push(0, 40); yield return null;
                    Assert.Greater(Vector3.Distance(stopped, _hero.transform.position), .02f);
                    _input.EndSimulatedHold(0);
                }
            }
        }
        [UnityTest]
        public IEnumerator MovementUsesConfiguredSpeedAndCollision_AndBlockedMatchRejectsHeldInput()
        {
            yield return Load(true);
            HeroLocomotion locomotion = _hero.GetComponent<HeroLocomotion>();
            _hero.GetComponent<HeroLocomotion>().WarpTo(new Vector3(-4f, 0f, -4f));
            yield return null;
            var agent = _hero.GetComponent<UnityEngine.AI.NavMeshAgent>();
            float speed = agent.speed;
            Vector3 start = _hero.transform.position;
            Push(0, _input.ContinuousRouter.JoystickRadiusPixels);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(Vector3.Distance(start, _hero.transform.position), Is.EqualTo(speed * 4f / 60f).Within(.015f));
            _input.EndSimulatedHold(0);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = _hero.transform.position + Vector3.forward * .9f + Vector3.up;
                wall.transform.localScale = new Vector3(4f, 2f, .3f);
                Physics.SyncTransforms();
                start = _hero.transform.position;
                Push(0, _input.ContinuousRouter.JoystickRadiusPixels);
                for (int i = 0; i < 12; i++) yield return null;
                Assert.Less(_hero.transform.position.z - start.z, .43f, "搖桿位移不可繞過原SphereCast碰撞");
                _input.EndSimulatedHold(0);
                _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
                Assert.IsTrue(_bootstrap.HeroInputBlockedForLab);
                Push(0, 40);
                start = _hero.transform.position;
                yield return null; yield return null;
                Assert.AreEqual(0f, _input.ContinuousRouter.MoveY);
                Assert.Less(Vector3.Distance(start, _hero.transform.position), .001f);
                locomotion.SetMovementLocked(true);
                Assert.AreEqual(Vector3.zero, locomotion.ApplyDisplacement(Vector3.right));
                locomotion.SetMovementLocked(false);
            }
            finally { Object.Destroy(wall); }
        }
        [UnityTest]
        public IEnumerator RealQuicksandRootAndVentFlight_BlockContinuousStickThroughHeroPipeline()
        {
            yield return Load();
            HeroLocomotion locomotion = _hero.GetComponent<HeroLocomotion>();
            Vector3 start = _hero.transform.position;
            Assert.GreaterOrEqual(_bootstrap.ElementField.CastWater(start, (int)Faction.RedTeam), 0);
            _bootstrap.ElementField.NotifyWallActivated(start, Faction.RedTeam);
            yield return null;
            Assert.GreaterOrEqual(_hero.HostileQuicksandZoneId, 0);
            Assert.IsTrue(locomotion.IsMovementLocked);
            Push(0, 40);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less(Vector3.Distance(start, _hero.transform.position), .001f, "真流沙縛足必須擋住搖桿");
            _input.EndSimulatedHold(0);
            ITerrainQuery terrain = locomotion.TerrainQuery;
            locomotion.SetTerrain(CanyonTerrainSpec.V0140);
            start = _hero.transform.position;
            locomotion.BeginVentFlight(start.x + 3f, start.z);
            Assert.IsTrue(locomotion.IsVentFlying);
            Push(-40, 0);
            _input.BeginSimulatedHold(1, Screen.width * .65f, Screen.height * .5f);
            _input.MoveSimulatedHold(1, Screen.width * .8f, Screen.height * .5f);
            float yaw = _lab.YawDegrees;
            yield return null; yield return null;
            Assert.IsTrue(locomotion.IsVentFlying);
            Assert.AreEqual(0f, _input.ContinuousRouter.MoveX);
            Assert.AreEqual(yaw, _lab.YawDegrees, "飛行限制期間不得吃look");
            _input.EndSimulatedHold(0); _input.EndSimulatedHold(1);
            locomotion.WarpTo(start); locomotion.SetTerrain(terrain);
        }
        private sealed class GameViewSize : IDisposable
        {
            private readonly EditorWindow _view;
            private readonly object _group;
            private readonly MethodInfo _select;
            private readonly int _original, _temporary;
            public GameViewSize(int width, int height)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                Assembly a = typeof(EditorWindow).Assembly;
                Type viewType = a.GetType("UnityEditor.GameView", true);
                _view = EditorWindow.GetWindow(viewType);
                _original = (int)viewType.GetProperty("selectedSizeIndex", flags).GetValue(_view);
                _select = viewType.GetMethod("SizeSelectionCallback", flags);
                Type sizesType = a.GetType("UnityEditor.GameViewSizes", true);
                object sizes = sizesType.BaseType.GetProperty("instance", flags).GetValue(null);
                object groupType = viewType.GetProperty("currentSizeGroupType", flags).GetValue(null);
                _group = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new[] { groupType });
                object resolution = Enum.Parse(a.GetType("UnityEditor.GameViewSizeType", true), "FixedResolution");
                object size = Activator.CreateInstance(a.GetType("UnityEditor.GameViewSize", true), new object[] { resolution, width, height, "Touch lab temporary size" });
                _temporary = (int)_group.GetType().GetMethod("GetTotalCount").Invoke(_group, null);
                _group.GetType().GetMethod("AddCustomSize").Invoke(_group, new[] { size });
                _select.Invoke(_view, new object[] { _temporary, null }); _view.Repaint();
            }
            public void Dispose()
            {
                try { _select.Invoke(_view, new object[] { _original, null }); _view.Repaint(); }
                finally { _group.GetType().GetMethod("RemoveCustomSize").Invoke(_group, new object[] { _temporary }); }
            }
        }
        [UnityTest]
        public IEnumerator FocusCancelSwitchDisableAndDeath_ClearHeldMove_WithoutResumeFromOldFinger()
        {
            yield return Load();
            Push(0, 40); yield return null;
            _input.SendMessage("OnApplicationFocus", false);
            Vector3 stop = _hero.transform.position;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(0f, _input.ContinuousRouter.MoveY);
            Assert.Less(Vector3.Distance(stop, _hero.transform.position), .001f);
            _input.EndSimulatedHold(0);
            Push(0, 40); yield return null;
            _input.CancelSimulatedHold(0);
            stop = _hero.transform.position;
            yield return null; yield return null;
            Assert.AreEqual(0f, _input.ContinuousRouter.MoveY);
            Assert.Less(Vector3.Distance(stop, _hero.transform.position), .001f);
            Push(0, 40); yield return null;
            _lab.SetThirdPerson(false);
            Assert.IsFalse(_input.ContinuousRouter.ThirdPersonEnabled);
            stop = _hero.transform.position;
            yield return null;
            Assert.Less(Vector3.Distance(stop, _hero.transform.position), .001f);
            _input.EndSimulatedHold(0); _lab.SetThirdPerson(true);
            Push(0, 40); yield return null;
            _hero.TakeDuelDamage(100f);
            yield return null; yield return null;
            Assert.IsFalse(_hero.IsAlive); Assert.AreEqual(0, _input.ContinuousRouter.MoveY);
            _lab.enabled = false;
            Assert.IsFalse(_input.ContinuousRouter.ThirdPersonEnabled);
        }
    }
}
#endif
