#if UNITY_EDITOR
using System.Collections;
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
    // D4：按鈕路徑在 Update 夾區內送出（同 ZeroAllocationTests 的 H2 規則）。
    public sealed class LabButtonDriver : MonoBehaviour
    {
        internal PlayerInputService Input;
        internal CameraComparisonLab Lab;
        public int Frame;
        public int Locks;

        private void Update()
        {
            if (Input == null) return;
            Frame++;
            if (Frame % 40 == 1)
            {
                ScreenRegion a = Lab.ActionButtonLayout.Attack;
                Input.SendScreenTap((a.XMin + a.XMax) * .5f, (a.YMin + a.YMax) * .5f);
                if (Lab.LastAimTarget != null) Locks++;
            }
            else if (Frame % 40 == 21)
            {
                ScreenRegion d = Lab.ActionButtonLayout.Dash;
                Input.SendScreenTap((d.XMin + d.XMax) * .5f, (d.YMin + d.YMax) * .5f);
            }
        }
    }

    // docs/CAMERA_LAB_COMBAT_PLAN.md §3 凍結驗收 D（與 E 的真場景多指部分）。全部走真路由（SendScreenTap／SimulatedHold）。
    public sealed class CameraLabCombatPlayTests
    {
        private CameraComparisonLab _lab;
        private PlayerInputService _input;
        private HeroController _hero;
        private Phase1Bootstrap _bootstrap;

        private IEnumerator Load(bool third = true)
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null; yield return null;
            _lab = Object.FindObjectOfType<CameraComparisonLab>();
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _input = Object.FindObjectOfType<PlayerInputService>();
            _hero = _lab.FollowedHero;
            if (third) _lab.SetThirdPerson(true);
            yield return null;
        }

        [TearDown]
        public void Cleanup()
        {
            if (_input != null) { _input.EndSimulatedHold(0); _input.EndSimulatedHold(1); }
            if (_lab != null) _lab.enabled = false;
            Time.captureDeltaTime = 0f;
            AllocationProbe.Measuring = false;
        }

        private static Vector2 Center(ScreenRegion r) => new Vector2((r.XMin + r.XMax) * .5f, (r.YMin + r.YMax) * .5f);
        private void TapAttack() { Vector2 c = Center(_lab.ActionButtonLayout.Attack); _input.SendScreenTap(c.x, c.y); }
        private void TapDash() { Vector2 c = Center(_lab.ActionButtonLayout.Dash); _input.SendScreenTap(c.x, c.y); }

        private IEnumerator WarpAndSettle(Vector3 position)
        {
            _hero.GetComponent<HeroLocomotion>().WarpTo(position);
            yield return null; yield return null;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        [UnityTest] // D1
        public IEnumerator D1_AttackButton_LocksDummyAhead_AndHitsIt()
        {
            yield return Load();
            Assert.IsTrue(_lab.ActionButtonsActive);
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);
            Assert.AreEqual(0f, _lab.YawDegrees);
            float health = dummy.Health;
            TapAttack();
            Assert.AreEqual(1, _lab.AimAttackCount);
            Assert.AreSame(dummy, _lab.LastAimTarget, "準星錐應挑正前方木樁");
            Assert.AreSame(dummy, _hero.CurrentTarget, "按下當下就走原普攻鎖定");
            float deadline = Time.time + 1f;
            while (dummy.Health >= health && Time.time < deadline) yield return null;
            Assert.Less(dummy.Health, health, "1.0s 內必須真的命中");
        }

        [UnityTest] // D1／E：搖桿按住推動中同時按 ATK
        public IEnumerator D1_StickHeldWhileAttack_StillLocksDummy_AndKeepsStick()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3.5f);
            Vector2 stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
            _input.BeginSimulatedHold(0, stick.x, stick.y);
            _input.MoveSimulatedHold(0, stick.x + 8f, stick.y);
            yield return null;
            Assert.IsTrue(_input.ContinuousRouter.MoveHeld);
            float moveX = _input.ContinuousRouter.MoveX;
            Assert.Greater(moveX, 0f);
            TapAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget);
            Assert.IsTrue(_input.ContinuousRouter.MoveHeld, "ATK 不得搶走搖桿");
            Assert.AreEqual(moveX, _input.ContinuousRouter.MoveX);
            _input.EndSimulatedHold(0);
        }

        [UnityTest] // D2
        public IEnumerator D2_DashButton_Moves1_4mAlongCameraForward_AndFollowsYaw()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            int charges = _hero.Mover.CurrentCharges;
            Assert.AreEqual(3, charges);
            Vector3 start = Flat(_hero.transform.position);
            TapDash();
            Assert.AreEqual(ActiveDashOutcome.FreeDash, _lab.LastDashOutcome);
            for (int i = 0; i < 18; i++) yield return null;
            Vector3 delta = Flat(_hero.transform.position) - start;
            Debug.Log("[CAMERA LAB TEST] D2 yaw0 delta=" + delta.ToString("F4"));
            Assert.That(delta.z, Is.EqualTo(1.4f).Within(.05f));
            Assert.Less(Mathf.Abs(delta.x), .05f);
            Assert.AreEqual(charges - 1, _hero.Mover.CurrentCharges);

            for (int i = 0; i < 66; i++) yield return null; // 讓 1.0s 連段窗口過期
            _lab.RotateThirdPerson(90f);
            start = Flat(_hero.transform.position);
            int before = _hero.Mover.CurrentCharges;
            TapDash();
            for (int i = 0; i < 18; i++) yield return null;
            delta = Flat(_hero.transform.position) - start;
            Debug.Log("[CAMERA LAB TEST] D2 yaw90 delta=" + delta.ToString("F4"));
            Assert.That(delta.x, Is.EqualTo(1.4f).Within(.05f));
            Assert.Less(Mathf.Abs(delta.z), .05f);
            Assert.AreEqual(before - 1, _hero.Mover.CurrentCharges);
        }

        [UnityTest] // E（真場景）：轉頭按住拖曳中同時按 DASH
        public IEnumerator E_LookHeldWhileDash_BothWork()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            _input.BeginSimulatedHold(1, Screen.width * .6f, Screen.height * .55f);
            _input.MoveSimulatedHold(1, Screen.width * .7f, Screen.height * .55f);
            yield return null;
            Assert.Greater(_lab.YawDegrees, 0f);
            float yaw = _lab.YawDegrees;
            Vector3 start = Flat(_hero.transform.position);
            TapDash();
            Assert.AreEqual(1, _lab.ActiveDashCount);
            _input.MoveSimulatedHold(1, Screen.width * .75f, Screen.height * .55f);
            for (int i = 0; i < 18; i++) yield return null;
            Assert.Greater(_lab.YawDegrees, yaw, "DASH 不得中斷轉頭");
            Assert.That((Flat(_hero.transform.position) - start).magnitude, Is.EqualTo(1.4f).Within(.05f));
            _input.EndSimulatedHold(1);
        }

        private static RuneWall AliveWall(RuneCaster caster)
        {
            foreach (RuneWall wall in caster.Pool) if (wall.IsAlive) return wall;
            return null;
        }

        private IEnumerator DragRuneAndRelease(Vector2 screenDirection, RuneGhostPreview ghost, float yaw)
        {
            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter);
            Vector2 rune = Center(layout.Button);
            float reach = _input.RuneSaturationPixels * 1.2f;
            _input.BeginSimulatedHold(0, rune.x, rune.y);
            _input.MoveSimulatedHold(0, rune.x + screenDirection.x * reach, rune.y + screenDirection.y * reach);
            yield return null;
            RuneCaster caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNull(AliveWall(caster), "放手前不得成牆");
            Vector3 ghostPosition = ghost.transform.position;
            _input.EndSimulatedHold(0);
            RuneWall wall = AliveWall(caster);
            Assert.IsNotNull(wall, "放手成牆");
            CameraLabAim.GroundForward(yaw, out float ax, out float az);
            Vector3 aim = new Vector3(ax, 0f, az);
            Vector3 expected = Flat(_hero.transform.position) + aim * 8f;
            Debug.Log("[CAMERA LAB TEST] D3 yaw" + yaw + " wall=" + wall.transform.position.ToString("F3") + " expected=" + expected.ToString("F3"));
            Assert.Less(Vector3.Distance(Flat(wall.transform.position), expected), .1f, "牆在準星前方 8m");
            Assert.GreaterOrEqual(Vector3.Dot(Flat(wall.transform.forward).normalized, aim), .999f, "牆正對鏡頭前方");
            Assert.Less(Vector3.Distance(Flat(ghostPosition), Flat(wall.transform.position)), .05f, "虛影與實牆同一換算");
        }

        [UnityTest] // D3
        public IEnumerator D3_RuneDragInThirdPerson_PlacesWallAlongCameraForward()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            RuneGhostPreview ghost = Object.FindObjectOfType<RuneGhostPreview>();
            RuneCaster caster = Object.FindObjectOfType<RuneCaster>();
            yield return DragRuneAndRelease(new Vector2(0f, 1f), ghost, 0f);
            caster.ResetForRound();
            yield return null;
            _lab.RotateThirdPerson(90f);
            yield return WarpAndSettle(new Vector3(-6f, 0f, -2f));
            yield return DragRuneAndRelease(new Vector2(-1f, 0f), ghost, 90f);
        }

        [UnityTest] // D5
        public IEnumerator D5_TopDown_ButtonsInactive_TapThereStaysWorld_RuneUsesScreenDirection()
        {
            yield return Load(false);
            Assert.IsFalse(_lab.IsThirdPerson);
            Assert.IsFalse(_lab.ActionButtonsActive);
            int moves = 0, ui = 0;
            _input.OnMoveDestinationSelected += _ => moves++;
            _input.OnUiRegionTapped += _ => ui++;
            Vector2 atk = Center(_lab.ActionButtonLayout.Attack);
            TouchRoute route = _input.Routing.Route(atk.x, atk.y, Screen.width, Screen.height, _input.ActiveMode, out _);
            Debug.Log("[CAMERA LAB TEST] D5 route at ATK=" + route);
            _input.SendScreenTap(atk.x, atk.y);
            Assert.AreEqual(0, _lab.AimAttackCount);
            Assert.AreEqual(0, _lab.ActiveDashCount);
            Assert.AreEqual(TouchRoute.World, route, "俯視下該位置是世界路由");
            Assert.AreEqual(1, moves, "俯視點該位置＝原點地移動");
            Assert.AreEqual(0, ui);

            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter);
            Vector2 rune = Center(layout.Button);
            _input.BeginSimulatedHold(0, rune.x, rune.y);
            _input.MoveSimulatedHold(0, rune.x - _input.RuneSaturationPixels * 1.2f, rune.y);
            yield return null;
            _input.EndSimulatedHold(0);
            RuneWall wall = AliveWall(Object.FindObjectOfType<RuneCaster>());
            Assert.IsNotNull(wall);
            Vector3 dir = RuneCaster.ScreenToWorldGroundDirection(new Vector2(-1f, 0f), Camera.main.transform);
            RuneCastLogic logic = new RuneCastLogic(new RuneTuning());
            Vector3 hero = _hero.transform.position;
            Assert.IsTrue(logic.TryDragPlacement(hero.x, hero.z, dir.x, dir.z, 1f, out RuneWallPlacement expected));
            Assert.That(wall.transform.position.x, Is.EqualTo(expected.CenterX).Within(.01f));
            Assert.That(wall.transform.position.z, Is.EqualTo(expected.CenterZ).Within(.01f));
        }

        [UnityTest] // D4
        public IEnumerator D4_ThirdPersonButtons_UpdateAndLateUpdate_AllocateNothing()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);

            GameObject driverObject = new GameObject("LabButtonDriver");
            LabButtonDriver driver = driverObject.AddComponent<LabButtonDriver>();
            driver.Lab = _lab;
            driver.Input = _input;
            for (int i = 0; i < 90; i++) yield return null; // 暖機：兩種按鈕各走過至少一次
            Assert.Greater(_lab.ActiveDashCount, 0, "暖機沒有真的滑步");
            Assert.Greater(driver.Locks, 0, "暖機沒有真的鎖定");

            AllocationProbe.Reset();
            GameObject rig = new GameObject("LabProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            rig.AddComponent<AllocationProbeEnd>();
            yield return null;
            int dashesBefore = _lab.ActiveDashCount, locksBefore = driver.Locks;
            AllocationProbe.Measuring = true;
            for (int i = 0; i < 180; i++) yield return null;
            AllocationProbe.Measuring = false;
            Object.Destroy(rig);
            Object.Destroy(driverObject);

            Assert.GreaterOrEqual(AllocationProbe.Frames, 180);
            Assert.Greater(_lab.ActiveDashCount, dashesBefore, "窗口內沒有滑步起手，0 byte 沒有鑑別力");
            Assert.Greater(driver.Locks, locksBefore, "窗口內沒有準星鎖定，0 byte 沒有鑑別力");
            Assert.AreEqual(0L, AllocationProbe.UpdateBytes,
                "THIRD 按鈕 Update 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.UpdateBytes + " bytes");
            Assert.AreEqual(0L, AllocationProbe.LateUpdateBytes,
                "THIRD 按鈕 LateUpdate 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.LateUpdateBytes + " bytes");
        }
    }
}
#endif
