using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Vow.UI;

namespace Vow.Bootstrap
{
    // 僅 camera-lab 分支的比較工具；世界點擊、戰鬥與數值仍走原本組裝。
    [DefaultExecutionOrder(-900)]
    public sealed class CameraComparisonLab : MonoBehaviour
    {
        public const float ThirdPersonPitch = 25f;
        public const float ThirdPersonDistance = 8f;
        public const float ThirdPersonFieldOfView = 55f;
        public const float FocusHeight = 1f;
        private const float CollisionRadius = 0.25f;
        private readonly RaycastHit[] _hits = new RaycastHit[128];
        private readonly Rect[] _buttons = new Rect[4];
        private FollowCameraRig _rig;
        private Camera _camera;
        private HeroController _hero;
        private PlayerInputService _input;
        private Phase1Bootstrap _bootstrap;
        private int _region = -1;
        private bool _ready, _subscribed, _layoutAvailable;
        private bool _originalRigEnabled;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private float _originalFov, _yaw, _pitch = ThirdPersonPitch;
        private DebugHud _hud;
        public float PitchDegrees => _pitch;
        private bool InputPermitted => _hero != null && _hero.IsAlive
            && !_hero.GetComponent<HeroLocomotion>().IsVentFlying
            && !_bootstrap.HeroInputBlockedForLab;
        private Vector3 ReadContinuousMove()
        {
            if (!IsThirdPerson || !InputPermitted) { _input.ContinuousRouter.CancelContinuousTouches(); return Vector3.zero; }
            TouchGestureRouter r = _input.ContinuousRouter;
            return Quaternion.Euler(0f, _yaw, 0f) * new Vector3(r.MoveX, 0f, r.MoveY);
        }
        private int _width, _height;
        private CaptureMatchState _captureState;
        private bool _talentVisible;
        private Rect _panel, _title, _help;
        private GUIStyle _labelStyle, _buttonStyle;

        public bool IsThirdPerson { get; private set; }
        public bool IsReady => _ready;
        public float YawDegrees => _yaw;
        public HeroController FollowedHero => _hero;
        public int UiRegionId => _region;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Attach(scene); }

        private static void Attach(Scene scene)
        {
            if (scene.name != "VOW_Phase1_Greybox") return;
            Phase1Bootstrap bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            if (bootstrap != null && bootstrap.GetComponent<CameraComparisonLab>() == null)
                bootstrap.gameObject.AddComponent<CameraComparisonLab>();
        }

        private void Awake() { useGUILayout = false; }

        private IEnumerator Start()
        {
            // 版面Update須早於輸入採樣；註冊只在原Bootstrap/HUD完成Start後進行。
            yield return null;
            Initialize();
        }

        private void Initialize()
        {
            _bootstrap = GetComponent<Phase1Bootstrap>();
            _input = _bootstrap != null ? _bootstrap.InputService as PlayerInputService : null;
            _camera = Camera.main;
            _rig = _camera != null ? _camera.GetComponentInParent<FollowCameraRig>() : null;
            if (_input == null || _rig == null) { enabled = false; return; }
            // 對手不是 HeroController；仍明確按本地陣營選唯一英雄，避免日後多英雄選錯。
            foreach (HeroController candidate in Object.FindObjectsOfType<HeroController>())
            {
                if (candidate.HeroFaction != _input.LocalFaction) continue;
                if (_hero != null) { enabled = false; return; }
                _hero = candidate;
            }
            if (_hero == null) { enabled = false; return; }
            _region = _input.Routing.RegisterUiRegion(default);
            if (_region < 0) { Debug.LogWarning("[CAMERA LAB] No UI region available.", this); enabled = false; return; }
            _hud = Object.FindObjectOfType<DebugHud>();
            _ready = true;
            Subscribe();
            RefreshLayout();
        }

        private void OnEnable()
        {
            if (!_ready) return;
            Subscribe();
            RefreshLayout();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _input.OnUiRegionTapped += OnUiTapped;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_ready) return;
            SetThirdPerson(false);
            if (_input != null)
            {
                _input.Routing.InvalidateUiRegionTouches(_region);
                _input.Routing.SetUiRegionActive(_region, false);
                if (_subscribed) _input.OnUiRegionTapped -= OnUiTapped;
            }
            _subscribed = false;
        }

        public void SetThirdPerson(bool thirdPerson)
        {
            if (!_ready || thirdPerson == IsThirdPerson) return;
            if (thirdPerson)
            {
                _originalRigEnabled = _rig.enabled;
                _originalPosition = _rig.transform.position;
                _originalRotation = _rig.transform.rotation;
                _originalFov = _camera.fieldOfView;
                _pitch = ThirdPersonPitch;
                _yaw = 0f;
                _rig.enabled = false;
                IsThirdPerson = true;
                _camera.fieldOfView = ThirdPersonFieldOfView;
                _input.ContinuousRouter.SetThirdPersonEnabled(true);
                _hero.SetContinuousMoveSource(ReadContinuousMove, () => _input.ContinuousRouter.MoveIntentVersion);
                if (_hud != null) _hud.SetCameraLabThirdPerson(true);
                RefreshLayout();
                PositionThirdPerson();
            }
            else
            {
                IsThirdPerson = false;
                _input.ContinuousRouter.SetThirdPersonEnabled(false);
                _hero.SetContinuousMoveSource(null);
                if (_hud != null) _hud.SetCameraLabThirdPerson(false);
                if (_rig != null)
                {
                    _rig.transform.SetPositionAndRotation(_originalPosition, _originalRotation);
                    _rig.enabled = _originalRigEnabled;
                    if (_originalRigEnabled) _rig.SnapToTarget();
                }
                if (_camera != null) _camera.fieldOfView = _originalFov;
                RefreshLayout();
            }
        }

        public void RotateThirdPerson(float degrees)
        {
            if (!_ready || !IsThirdPerson) return;
            _yaw = Mathf.Repeat(_yaw + degrees, 360f);
            PositionThirdPerson();
        }

        public void ResetThirdPerson()
        {
            if (!IsThirdPerson) return;
            _yaw = 0f;
            _pitch = ThirdPersonPitch;
            PositionThirdPerson();
        }

        private void Update()
        {
            if (_ready && (_width != Screen.width || _height != Screen.height || _captureState != _bootstrap.CaptureState
                || _talentVisible != _bootstrap.TalentPanelVisible))
                RefreshLayout();
        }

        private void LateUpdate()
        {
            if (!_ready || !IsThirdPerson) return;
            TouchGestureRouter router = _input.ContinuousRouter;
            if (!InputPermitted) router.CancelContinuousTouches();
            float sensitivity = 2.5f / Mathf.Clamp(_input.PixelsPerMillimeter, 3f, 25f);
            _yaw = Mathf.Repeat(_yaw + router.LookDeltaX * sensitivity, 360f);
            _pitch = Mathf.Clamp(_pitch - router.LookDeltaY * sensitivity, 10f, 50f);
            router.ConsumeLook();
            PositionThirdPerson();
        }

        private void PositionThirdPerson()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = _hero.transform.position + Vector3.up * FocusHeight;
            Vector3 direction = -(rotation * Vector3.forward);
            int count = Physics.SphereCastNonAlloc(focus, CollisionRadius, direction, _hits,
                ThirdPersonDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float distance = ThirdPersonDistance;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(_hero.transform)) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, _hits[i].distance - 0.05f));
            }
            // 緩衝滿時不猜漏掉的牆：保守回焦點。
            if (count == _hits.Length) distance = 0f;
            _rig.transform.SetPositionAndRotation(focus + direction * distance, rotation);
        }

        private void RefreshLayout()
        {
            if (IsThirdPerson) _input.ContinuousRouter.CancelActiveTouches();
            _width = Screen.width;
            _height = Screen.height;
            _captureState = _bootstrap.CaptureState;
            _talentVisible = _bootstrap.TalentPanelVisible;
            DebugHudLayout hud = DebugHudLayout.Compute(_width, _height, Screen.dpi, true, true, true,
                _captureState != CaptureMatchState.Off, _captureState == CaptureMatchState.Active);
            float scale = hud.Scale;
            float x = 268f * scale;
            RuneButtonLayout rune = RuneButtonLayout.Compute(_width, _height, _input.PixelsPerMillimeter);
            float right = Mathf.Min((hud.MatchPanel.X - 20f) * scale, rune.Button.XMin - 8f);
            float width = right - x;
            bool oneRow = width >= 300f;
            float y = (hud.TalentPanel.YMax + 6f) * scale;
            if (_captureState == CaptureMatchState.Active && _height / scale > 400f) y = (hud.Water.YMax + 8f) * scale;
            // 小橫向視窗借用尚未顯示人才盤的空位；人才盤出現時優先保留原HUD。
            if (_height / scale < 400f && !oneRow && !_talentVisible) y = 84f * scale;
            float height = oneRow ? 92f : 138f;
            _layoutAvailable = width >= 156f && y + height < _height - 12f;
            if (_captureState == CaptureMatchState.Active && _height / scale <= 400f
                && y + height > (hud.Water.YMin - 8f) * scale) _layoutAvailable = false;
            if (_width <= _height && IsThirdPerson) SetThirdPerson(false);
            _panel = new Rect(x, y, width, height);
            if (!_layoutAvailable) _panel = new Rect(_width / 2f - 100f, _height - 38f, 200f, 24f);
            _title = new Rect(x + 4f, y + 2f, width - 8f, 22f);
            _help = new Rect(x + 4f, y + height - 23f, width - 8f, 22f);
            int columns = oneRow ? 4 : 2;
            float buttonWidth = (width - 10f - (columns - 1) * 4f) / columns;
            for (int i = 0; i < 4; i++)
                _buttons[i] = new Rect(x + 5f + (i % columns) * (buttonWidth + 4f),
                    y + 26f + (i / columns) * 46f, buttonWidth, 42f);
            if (IsThirdPerson)
            {
                float unit = Mathf.Min(_width / 844f, _height / 390f);
                _layoutAvailable = _width > _height;
                _panel = new Rect(8f * unit, 8f * unit, 310f * unit, 70f * unit);
                _title = new Rect(_panel.x + 4f, _panel.y, _panel.width - 8f, 20f * unit);
                _help = new Rect(_panel.x + 4f, _panel.y + 50f * unit, _panel.width - 8f, 20f * unit);
                for (int i = 0; i < 4; i++) _buttons[i] = new Rect(_panel.x + (4f + i * 76f) * unit, _panel.y + 22f * unit, 72f * unit, 28f * unit);
                _input.ContinuousRouter.MoveZone = new ScreenRegion(0f, 0f, _width * 0.42f, _height * 0.55f);
                _input.ContinuousRouter.JoystickRadiusPixels = 55f * unit;
            }
            _input.Routing.InvalidateUiRegionTouches(_region);
            _input.Routing.UpdateUiRegion(_region, new ScreenRegion(_panel.xMin, _height - _panel.yMax, _panel.xMax, _height - _panel.yMin));
            _input.Routing.SetUiRegionActive(_region, enabled);
        }

        public bool TryGetButtonScreenPoint(int index, out Vector2 point)
        {
            point = Vector2.zero;
            if (!_ready || !_layoutAvailable || !enabled || index < 0 || index >= 4) return false;
            point = new Vector2(_buttons[index].center.x, _height - _buttons[index].center.y);
            return true;
        }

        private void OnUiTapped(int id)
        {
            if (id != _region || !_layoutAvailable) return;
            Vector2 point = _input.UiTapScreenPosition;
            point.y = _height - point.y;
            if (_buttons[0].Contains(point)) SetThirdPerson(!IsThirdPerson);
            else if (_buttons[1].Contains(point)) RotateThirdPerson(-30f);
            else if (_buttons[2].Contains(point)) RotateThirdPerson(30f);
            else if (_buttons[3].Contains(point)) ResetThirdPerson();
        }

        private void OnGUI()
        {
            if (!_ready) return;
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                _buttonStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            }
            if (!_layoutAvailable)
            {
                GUI.Label(_panel, "CAMERA LAB: use landscape", _labelStyle);
                return;
            }
            GUI.Box(_panel, GUIContent.none);
            GUI.Label(_title, IsThirdPerson ? "CAMERA LAB | 3RD" : "CAMERA LAB | TOP", _labelStyle);
            GUI.Box(_buttons[0], IsThirdPerson ? "TOP DOWN" : "THIRD", _buttonStyle);
            GUI.Box(_buttons[1], "LEFT", _buttonStyle);
            GUI.Box(_buttons[2], "RIGHT", _buttonStyle);
            GUI.Box(_buttons[3], "RESET", _buttonStyle);
            if (IsThirdPerson)
            {
                TouchGestureRouter r = _input.ContinuousRouter;
                float unit = Mathf.Min(_width / 844f, _height / 390f);
                Vector2 origin = r.MoveHeld ? new Vector2(r.MoveOriginX, _height - r.MoveOriginY) : new Vector2(92f * unit, _height - 90f * unit);
                float radius = r.JoystickRadiusPixels;
                GUI.Box(new Rect(origin.x - radius, origin.y - radius, radius * 2f, radius * 2f), "MOVE", _labelStyle);
                GUI.Box(new Rect(origin.x + r.MoveX * radius - 14f, origin.y - r.MoveY * radius - 14f, 28f, 28f), GUIContent.none);
            }
            GUI.Label(_help, IsThirdPerson ? "Left: move | Right: look / tap enemy" : _panel.width >= 300f ? "Tap ground: move | enemy: attack" : "Tap ground / enemy", _labelStyle);
        }
    }
}
