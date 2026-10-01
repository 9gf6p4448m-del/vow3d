using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Vow.Bootstrap;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
#endif

namespace Vow.Tests.PlayMode
{
    public sealed class CameraComparisonLabTests
    {
        private CameraComparisonLab _lab;
        private PlayerInputService _input;
        private FollowCameraRig _rig;
        private HeroController _hero;
        private Camera _camera;
        private GameObject _wall;

        private IEnumerator Load()
        {
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _lab = Object.FindObjectOfType<CameraComparisonLab>();
            Assert.IsNotNull(_lab, "灰盒場景必須自動安裝比較工具");
            Assert.IsTrue(_lab.IsReady);
            _input = Object.FindObjectOfType<PlayerInputService>();
            _hero = _lab.FollowedHero;
            _camera = Camera.main;
            _rig = _camera.GetComponentInParent<FollowCameraRig>();
            Assert.AreEqual(_input.LocalFaction, _hero.HeroFaction);
        }

        [TearDown]
        public void CleanUp()
        {
            if (_lab != null) _lab.enabled = false;
            if (_wall != null) Object.Destroy(_wall);
        }

        private Vector2 ButtonPoint(int button)
        {
            Assert.IsTrue(_lab.TryGetButtonScreenPoint(button, out Vector2 point));
            return point;
        }

        private void TapButton(int button)
        {
            Vector2 point = ButtonPoint(button);
            _input.SendScreenTap(point.x, point.y);
        }

        [UnityTest]
        public IEnumerator DefaultTopDown_AndRoundTrip_PreserveOriginalRigAndHero()
        {
            yield return Load();
            Assert.IsFalse(_lab.IsThirdPerson);
            Assert.IsTrue(_rig.enabled);
            Assert.AreEqual(40f, _camera.fieldOfView);
            Quaternion originalRotation = _rig.transform.rotation;
            Vector3 originalPosition = _rig.transform.position;
            Vector3 heroPosition = _hero.transform.position;
            float health = _hero.Health;
            PlayerState state = _hero.StateMachine.CurrentState;
            TapButton(0);
            Assert.IsTrue(_lab.IsThirdPerson);
            Assert.IsFalse(_rig.enabled);
            Assert.AreEqual(55f, _camera.fieldOfView);
            TapButton(0);
            Assert.IsFalse(_lab.IsThirdPerson);
            Assert.IsTrue(_rig.enabled);
            Assert.AreEqual(40f, _camera.fieldOfView);
            Assert.Less(Quaternion.Angle(originalRotation, _rig.transform.rotation), 0.001f);
            Assert.Less(Vector3.Distance(originalPosition, _rig.transform.position), 0.001f);
            Assert.AreEqual(heroPosition, _hero.transform.position);
            Assert.AreEqual(health, _hero.Health);
            Assert.AreEqual(state, _hero.StateMachine.CurrentState);
        }

        [UnityTest]
        public IEnumerator AllButtons_UseOneUiRegion_WithoutWorldTap_AndFollowHero()
        {
            yield return Load();
            int moveEvents = 0, targetEvents = 0;
            _input.OnMoveDestinationSelected += _ => moveEvents++;
            _input.OnCombatTargetSelected += _ => targetEvents++;
            // 修補 r1 F4/F6：TOP 只有切換鈕（0）佔 UI 區；LEFT/RIGHT/RESET（1~3）只在 THIRD 存在。
            Vector2 toggle = ButtonPoint(0);
            Assert.AreEqual(TouchRoute.UiRegion,
                _input.Routing.Route(toggle.x, toggle.y, Screen.width, Screen.height, _input.ActiveMode, out int topId));
            Assert.AreEqual(_lab.UiRegionId, topId);
            for (int i = 1; i < 4; i++)
                Assert.IsFalse(_lab.TryGetButtonScreenPoint(i, out _), "TOP 不該有第 " + i + " 顆鈕");
            TapButton(0);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = ButtonPoint(i);
                Assert.AreEqual(TouchRoute.UiRegion,
                    _input.Routing.Route(p.x, p.y, Screen.width, Screen.height, _input.ActiveMode, out int id));
                Assert.AreEqual(_lab.UiRegionId, id);
            }
            TapButton(1);
            Assert.AreEqual(330f, _lab.YawDegrees);
            TapButton(2);
            Assert.AreEqual(0f, _lab.YawDegrees);
            TapButton(2);
            Assert.AreEqual(30f, _lab.YawDegrees);
            TapButton(3);
            Assert.AreEqual(0f, _lab.YawDegrees);
            _hero.GetComponent<HeroLocomotion>().WarpTo(_hero.transform.position + Vector3.right);
            yield return null;
            Vector3 focus = _hero.transform.position + Vector3.up * CameraComparisonLab.FocusHeight;
            Vector3 offset = focus - _rig.transform.position;
            Assert.LessOrEqual(offset.magnitude, CameraComparisonLab.ThirdPersonDistance + 0.001f);
            Assert.Less(Vector3.Cross(offset, _rig.transform.forward).magnitude, 0.002f);
            Assert.Less(Quaternion.Angle(_rig.transform.rotation, Quaternion.Euler(25f, 0f, 0f)), 0.001f);
            Assert.AreEqual(0, moveEvents);
            Assert.AreEqual(0, targetEvents);
        }

#if UNITY_EDITOR
        // 修補 r1 F4/F6：TOP 時原本 LEFT/RIGHT/RESET 那三格不再是 UI 區——點下去是點地移動（不被面板吃掉、不轉鏡頭）；
        // 切 THIRD 四顆鈕照舊、切回 TOP 又只剩切換鈕。修正前版本這三格路由成 UiRegion，本測試紅。
        [UnityTest]
        public IEnumerator TopMode_OnlyToggleIsUi_FormerLabButtonAreasReachTheWorld()
        {
            yield return Load();
            Rect[] buttons = (Rect[])typeof(CameraComparisonLab)
                .GetField("_buttons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_lab);
            int moveEvents = 0;
            _input.OnMoveDestinationSelected += _ => moveEvents++;
            for (int i = 1; i < 4; i++)
            {
                float x = buttons[i].center.x, y = Screen.height - buttons[i].center.y;
                Assert.AreNotEqual(TouchRoute.UiRegion,
                    _input.Routing.Route(x, y, Screen.width, Screen.height, _input.ActiveMode, out _),
                    "TOP 時第 " + i + " 格不該被面板吃掉");
                int before = moveEvents;
                _input.SendScreenTap(x, y);
                Assert.AreEqual(before + 1, moveEvents, "TOP 時點第 " + i + " 格應是點地移動");
                Assert.IsFalse(_lab.IsThirdPerson);
                Assert.AreEqual(0f, _lab.YawDegrees);
            }
            TapButton(0);
            Assert.IsTrue(_lab.IsThirdPerson);
            for (int i = 0; i < 4; i++) Assert.IsTrue(_lab.TryGetButtonScreenPoint(i, out _), "THIRD 四顆鈕都在");
            TapButton(0);
            Assert.IsFalse(_lab.IsThirdPerson);
            for (int i = 1; i < 4; i++) Assert.IsFalse(_lab.TryGetButtonScreenPoint(i, out _), "切回 TOP 後只剩切換鈕");
            buttons = (Rect[])typeof(CameraComparisonLab)
                .GetField("_buttons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_lab);
            Vector2 left = new Vector2(buttons[1].center.x, Screen.height - buttons[1].center.y);
            Assert.AreNotEqual(TouchRoute.UiRegion,
                _input.Routing.Route(left.x, left.y, Screen.width, Screen.height, _input.ActiveMode, out _), "切回 TOP 後 UI 區縮回切換鈕");
        }
#endif

        [UnityTest]
        public IEnumerator WallContractsCamera_AndHeroColliderDoesNotBlockIt()
        {
            yield return Load();
            _lab.SetThirdPerson(true);
            Vector3 focus = _hero.transform.position + Vector3.up;
            Vector3 direction = -_rig.transform.forward;
            float freeDistance = Vector3.Distance(focus, _rig.transform.position);
            Assert.Greater(freeDistance, 1f, "英雄自己的Collider不可把鏡頭縮到焦點");
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "CameraLabTestWall";
            _wall.transform.SetPositionAndRotation(focus + direction * (freeDistance * 0.6f), _rig.transform.rotation);
            _wall.transform.localScale = new Vector3(4f, 4f, 0.5f);
            Physics.SyncTransforms();
            yield return null;
            float blockedDistance = Vector3.Distance(focus, _rig.transform.position);
            Assert.Less(blockedDistance, freeDistance * 0.6f - 0.25f);
            Assert.Greater(blockedDistance, 0.1f);
            Assert.Greater(Vector3.Distance(_camera.transform.position,
                _wall.GetComponent<Collider>().ClosestPoint(_camera.transform.position)), 0.24f);
            Collider destroyedCollider = _wall.GetComponent<Collider>();
            Object.Destroy(_wall);
            _wall = null;
            // Destroy延後至當幀結束；下一幀coroutine在Update後／LateUpdate前恢復。
            // 第一個yield只確認Collider已移除，鏡頭仍是移除前的上一次LateUpdate結果。
            yield return null;
            Assert.IsTrue(destroyedCollider == null, "必須先確認牆Collider已銷毀");
            int removedFrame = Time.frameCount;
            // 固定再跨一幀：恰好包含移除後的一次LateUpdate，不重試、不改距離斷言。
            yield return null;
            Assert.AreEqual(removedFrame + 1, Time.frameCount);
            Assert.That(Vector3.Distance(focus, _rig.transform.position), Is.EqualTo(freeDistance).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator DisableAndReenable_ReusesRegion_RestoresRig_AndUnsubscribes()
        {
            yield return Load();
            Vector2 point = ButtonPoint(0);
            int id = _lab.UiRegionId;
            TapButton(0);
            _lab.enabled = false;
            Assert.IsFalse(_lab.IsThirdPerson);
            Assert.IsTrue(_rig.enabled);
            Assert.AreEqual(40f, _camera.fieldOfView);
            ((ITouchGestureSink)_input).OnUiRegionTapped(id);
            Assert.IsFalse(_lab.IsThirdPerson, "停用後不再訂閱UI事件");
            Assert.AreNotEqual(TouchRoute.UiRegion,
                _input.Routing.Route(point.x, point.y, Screen.width, Screen.height, _input.ActiveMode, out int disabledId));
            for (int i = 0; i < 4; i++)
            {
                _lab.enabled = true;
                Assert.AreEqual(id, _lab.UiRegionId);
                TapButton(0);
                Assert.IsTrue(_lab.IsThirdPerson);
                _lab.enabled = false;
            }
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ResizeRealGameView_RefreshesRegionAndAllFourButtons()
        {
            yield return Load();
            int id = _lab.UiRegionId;
            Assert.Less(MonoImporter.GetExecutionOrder(MonoScript.FromMonoBehaviour(_lab)),
                MonoImporter.GetExecutionOrder(MonoScript.FromMonoBehaviour(_input)), "版面刷新必須早於輸入Update");
            using (var gameView = new TemporaryGameViewSize(844, 390))
            {
                yield return null;
                yield return null;
                Assert.AreEqual(844, Screen.width);
                Assert.AreEqual(390, Screen.height);
                int worldEvents = 0;
                _input.OnMoveDestinationSelected += _ => worldEvents++;
                _input.OnCombatTargetSelected += _ => worldEvents++;
                Assert.AreEqual(id, _lab.UiRegionId);
                TapButton(0);
                Assert.IsTrue(_lab.IsThirdPerson);
                TapButton(1);
                Assert.AreEqual(330f, _lab.YawDegrees);
                TapButton(2);
                Assert.AreEqual(0f, _lab.YawDegrees);
                TapButton(2);
                TapButton(3);
                Assert.AreEqual(0f, _lab.YawDegrees);
                using (var compact = new TemporaryGameViewSize(640, 360))
                {
                    // 僅等resize第一個Update，不手動刷新region；驗舊region不存在誤發world的窗口。
                    yield return null;
                    Assert.AreEqual(640, Screen.width);
                    Assert.AreEqual(360, Screen.height);
                    Assert.IsTrue(_lab.IsThirdPerson);
                    TapButton(0);
                    Assert.IsFalse(_lab.IsThirdPerson);
                    Assert.IsTrue(_rig.enabled);
                    Assert.AreEqual(40f, _camera.fieldOfView);
                }
                yield return null;
                Assert.AreEqual(0, worldEvents);
            }
            yield return null;
            yield return null;
            Assert.AreEqual(640, Screen.width);
            Assert.AreEqual(480, Screen.height);
            TapButton(0);
            Assert.IsTrue(_lab.IsThirdPerson);
        }

        // 與既有全域fixture相同：改真實render size，不拿SetResolution當成實際Screen尺寸。
        private sealed class TemporaryGameViewSize : IDisposable
        {
            private readonly EditorWindow _view;
            private readonly object _group;
            private readonly MethodInfo _select;
            private readonly int _original, _temporary;

            public TemporaryGameViewSize(int width, int height)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                Assembly assembly = typeof(EditorWindow).Assembly;
                Type viewType = assembly.GetType("UnityEditor.GameView", true);
                _view = EditorWindow.GetWindow(viewType);
                _original = (int)viewType.GetProperty("selectedSizeIndex", flags).GetValue(_view);
                _select = viewType.GetMethod("SizeSelectionCallback", flags);
                Type sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
                object sizes = sizesType.BaseType.GetProperty("instance", flags).GetValue(null);
                object groupType = viewType.GetProperty("currentSizeGroupType", flags).GetValue(null);
                _group = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new[] { groupType });
                Type sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
                object fixedResolution = Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType", true), "FixedResolution");
                object size = Activator.CreateInstance(sizeType, new object[] { fixedResolution, width, height, "Camera lab temporary size" });
                _temporary = (int)_group.GetType().GetMethod("GetTotalCount").Invoke(_group, null);
                _group.GetType().GetMethod("AddCustomSize").Invoke(_group, new[] { size });
                _select.Invoke(_view, new object[] { _temporary, null });
                _view.Repaint();
            }

            public void Dispose()
            {
                try { _select.Invoke(_view, new object[] { _original, null }); _view.Repaint(); }
                finally { _group.GetType().GetMethod("RemoveCustomSize").Invoke(_group, new object[] { _temporary }); }
            }
        }
#endif

        private sealed class CoordinateSink : ITouchGestureSink
        {
            public TouchGestureRouter Router;
            public readonly Vector2[] Points = new Vector2[2];
            public int Count, WorldCount;
            public void OnUiRegionTapped(int id) { Points[Count++] = new Vector2(Router.LastUiTapX, Router.LastUiTapY); }
            public void OnWorldTap(float x, float y) { WorldCount++; }
            public void OnCadenceFlick(float x, float y) { }
            public void OnRuneDragUpdated(float x, float y, float distance01) { }
            public void OnRuneQuickCast() { }
            public void OnRuneCancelled() { }
            public void OnRuneReleased(float x, float y, float distance01) { }
        }

        [Test]
        public void SimultaneousUiTouches_ReportEachValidReleaseCoordinate()
        {
            var routing = new InputRoutingManager();
            int region = routing.RegisterUiRegion(new ScreenRegion(100f, 100f, 400f, 300f));
            var sink = new CoordinateSink();
            var router = new TouchGestureRouter(routing, sink, ControlMode.ModeA_FullScreenFlick)
            { ScreenWidth = 640f, ScreenHeight = 480f };
            sink.Router = router;
            router.ProcessTouch(11, TouchPhaseKind.Began, 150f, 150f, 0, 0);
            router.ProcessTouch(12, TouchPhaseKind.Began, 300f, 250f, 0, 0);
            router.ProcessTouch(12, TouchPhaseKind.Ended, 310f, 260f, 0.1, 0.1);
            router.ProcessTouch(11, TouchPhaseKind.Ended, 160f, 160f, 0.2, 0.2);
            Assert.AreEqual(2, sink.Count);
            Assert.AreEqual(new Vector2(310f, 260f), sink.Points[0]);
            Assert.AreEqual(new Vector2(160f, 160f), sink.Points[1]);
            Assert.AreEqual(0, sink.WorldCount);
            router.ProcessTouch(13, TouchPhaseKind.Began, 150f, 150f, 0.3, 0.3);
            routing.InvalidateUiRegionTouches(region);
            router.ProcessTouch(13, TouchPhaseKind.Ended, 170f, 170f, 0.4, 0.4);
            Assert.AreEqual(2, sink.Count);
            Assert.AreEqual(160f, router.LastUiTapX);
            Assert.AreEqual(160f, router.LastUiTapY);
        }
    }
}
