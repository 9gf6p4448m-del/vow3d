using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vow.Combat;
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
        // ── 第三人稱戰鬥操作（docs/CAMERA_LAB_COMBAT_PLAN.md）──
        private static readonly string[] DashLabels = { "DASH 0", "DASH 1", "DASH 2", "DASH 3", "DASH 4" };
        private static readonly Color AttackColor = new Color(0.85f, 0.45f, 0.2f, 0.9f);
        private static readonly Color DashColor = new Color(0.3f, 0.7f, 0.45f, 0.9f);
        private static readonly Color EmptyColor = new Color(0.15f, 0.18f, 0.22f, 0.85f);
        private RuneCaster _runeCaster;
        private RuneGhostPreview _runeGhost;
        private System.Func<Vector3> _aimGround;
        private LabActionButtonLayout _actionLayout;
        private GUIStyle _actionLabel;
        public int AimAttackCount { get; private set; }
        public int ActiveDashCount { get; private set; }
        public ActiveDashOutcome LastDashOutcome { get; private set; }
        public ICombatTarget LastAimTarget { get; private set; }
        public LabActionButtonLayout ActionButtonLayout => _actionLayout;
        public bool ActionButtonsActive => _input != null && _input.ContinuousRouter.ActionButtonsEnabled;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool _logDash, _logWall;
        private Vector3 _dashStart;
#endif
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
            _runeCaster = Object.FindObjectOfType<RuneCaster>();
            _runeGhost = Object.FindObjectOfType<RuneGhostPreview>();
            _aimGround = AimGround; // 委派只在這裡建一次，執行期零配置
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
            _input.OnLabActionButton += OnActionButton;
#if UNITY_WEBGL && !UNITY_EDITOR
            _input.OnRuneCastReleased += OnRuneReleasedForLog;
#endif
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
                if (_subscribed)
                {
                    _input.OnUiRegionTapped -= OnUiTapped;
                    _input.OnLabActionButton -= OnActionButton;
#if UNITY_WEBGL && !UNITY_EDITOR
                    _input.OnRuneCastReleased -= OnRuneReleasedForLog;
#endif
                }
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
                if (_runeCaster != null) _runeCaster.SetDragDirectionOverride(_aimGround);
                if (_runeGhost != null) _runeGhost.SetDragDirectionOverride(_aimGround);
                if (_hud != null) _hud.SetCameraLabThirdPerson(true);
                RefreshLayout();
                PositionThirdPerson();
            }
            else
            {
                IsThirdPerson = false;
                _input.ContinuousRouter.SetThirdPersonEnabled(false);
                _hero.SetContinuousMoveSource(null);
                _input.ContinuousRouter.ActionButtonsEnabled = false;
                if (_runeCaster != null) _runeCaster.SetDragDirectionOverride(null);
                if (_runeGhost != null) _runeGhost.SetDragDirectionOverride(null);
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
#if UNITY_WEBGL && !UNITY_EDITOR
            LogPendingResults();
#endif
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
            _actionLayout = LabActionButtonLayout.Compute(_width, _height, _input.PixelsPerMillimeter);
            _input.ContinuousRouter.ActionButtons = _actionLayout;
            _input.ContinuousRouter.ActionButtonsEnabled = IsThirdPerson && _layoutAvailable && enabled;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 驗證用（Playwright 換算觸控座標）：只在版面重算時輸出，Unity 螢幕座標（原點左下）。
            RuneButtonLayout runeLog = RuneButtonLayout.Compute(_width, _height, _input.PixelsPerMillimeter);
            Debug.Log("[CAMERA LAB] LAYOUT third=" + IsThirdPerson + " w=" + _width + " h=" + _height
                + " toggle=" + Pt(_buttons[0].center.x, _height - _buttons[0].center.y)
                + " atk=" + Pt((_actionLayout.Attack.XMin + _actionLayout.Attack.XMax) * .5f, (_actionLayout.Attack.YMin + _actionLayout.Attack.YMax) * .5f)
                + " dash=" + Pt((_actionLayout.Dash.XMin + _actionLayout.Dash.XMax) * .5f, (_actionLayout.Dash.YMin + _actionLayout.Dash.YMax) * .5f)
                + " rune=" + Pt((runeLog.Button.XMin + runeLog.Button.XMax) * .5f, (runeLog.Button.YMin + runeLog.Button.YMax) * .5f)
                + " sat=" + _input.RuneSaturationPixels.ToString("F1") + " ppmm=" + _input.PixelsPerMillimeter.ToString("F2"));
#endif
            _input.Routing.InvalidateUiRegionTouches(_region);
            _input.Routing.UpdateUiRegion(_region, new ScreenRegion(_panel.xMin, _height - _panel.yMax, _panel.xMax, _height - _panel.yMin));
            _input.Routing.SetUiRegionActive(_region, enabled);
        }

        // 鏡頭水平前方（準星方向）。塑牆覆寫與 ATK／DASH 共用同一個來源。
        private Vector3 AimGround()
        {
            CameraLabAim.GroundForward(_yaw, out float x, out float z);
            return new Vector3(x, 0f, z);
        }

        private void OnActionButton(LabActionButton button)
        {
            if (!_ready || !IsThirdPerson || !InputPermitted) return;
            if (button == LabActionButton.Attack) AimAttack();
            else if (button == LabActionButton.Dash) ActiveDash();
        }

        private void AimAttack()
        {
            AimAttackCount++;
            LastAimTarget = null;
            CombatTargetRoster roster = _bootstrap.ElementRoster;
            if (roster == null) return;
            Vector3 origin = _hero.transform.position;
            CameraLabAim.GroundForward(_yaw, out float ax, out float az);
            AimTargetPicker picker = default;
            picker.Begin(origin.x, origin.z, ax, az, CameraLabAim.ConeHalfAngleDegrees, CameraLabAim.MaxAimDistance);
            for (int i = 0; i < roster.Count; i++)
            {
                CombatTargetBehaviour candidate = roster.GetBehaviour(i);
                if (candidate == null || !candidate.IsAlive || candidate.TargetTransform == null) continue;
                // 與點擊同規則：己方石牆不當目標（點擊會穿過去）；其餘交給英雄唯一的交戰判準。
                if (candidate.TargetFaction == Faction.DestructibleWall && candidate.OwnerFaction == _hero.HeroFaction) continue;
                if (!_hero.CanEngage(candidate)) continue;
                Vector3 p = candidate.TargetTransform.position;
                picker.Consider(i, p.x, p.z);
            }
            // 錐內優先；錐內沒有就退回 8m 內最近者，8m 內都沒有才不出手。
            int picked = picker.ResolvedIndex;
            if (picked < 0) return;
            LastAimTarget = roster.Get(picked);
            _input.SubmitCombatTarget(LastAimTarget);
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CAMERA LAB] ATK target=" + roster.GetBehaviour(picked).name + " cone=" + (picker.BestIndex >= 0));
#endif
        }

        private void ActiveDash()
        {
            TouchGestureRouter r = _input.ContinuousRouter;
            ActiveDashLogic.ResolveDirection(r.MoveX, r.MoveY, _yaw, out float x, out float z);
#if UNITY_WEBGL && !UNITY_EDITOR
            _dashStart = _hero.transform.position;
#endif
            LastDashOutcome = _hero.TryActiveDash(new Vector3(x, 0f, z));
            if (LastDashOutcome == ActiveDashOutcome.FreeDash || LastDashOutcome == ActiveDashOutcome.CadenceFlick) ActiveDashCount++;
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CAMERA LAB] DASH outcome=" + LastDashOutcome + " charges=" + _hero.Mover.CurrentCharges);
            _logDash = ActiveDashCount > 0 && (LastDashOutcome == ActiveDashOutcome.FreeDash || LastDashOutcome == ActiveDashOutcome.CadenceFlick);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 驗證用（Playwright 讀 console）：只在按鈕／放手時配置，不在每幀路徑上。
        private void OnRuneReleasedForLog(Vector2 direction, float distance) { _logWall = IsThirdPerson; }

        private static string Pt(float x, float y) { return "(" + x.ToString("F1") + "," + y.ToString("F1") + ")"; }

        private void LogPendingResults()
        {
            if (_logDash && !_hero.Mover.IsDashing)
            {
                _logDash = false;
                Vector3 d = _hero.transform.position - _dashStart;
                d.y = 0f;
                Debug.Log("[CAMERA LAB] DASH moved=" + d.magnitude.ToString("F3") + " dir=" + d.normalized.ToString("F3")
                    + " yaw=" + _yaw.ToString("F1"));
            }
            if (_logWall && _runeCaster != null)
            {
                _logWall = false;
                Vector3 hero = _hero.transform.position;
                Vector3 aim = AimGround();
                foreach (RuneWall wall in _runeCaster.Pool)
                {
                    if (wall == null || !wall.IsAlive) continue;
                    Vector3 c = wall.transform.position;
                    Debug.Log("[CAMERA LAB] WALL center=(" + c.x.ToString("F2") + "," + c.z.ToString("F2") + ") hero=("
                        + hero.x.ToString("F2") + "," + hero.z.ToString("F2") + ") aim=(" + aim.x.ToString("F3") + ","
                        + aim.z.ToString("F3") + ") facing=" + Vector3.Dot(wall.transform.forward, aim).ToString("F4"));
                }
            }
        }
#endif

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

        // 沿用 RuneButtonView 的 IMGUI 畫法（DrawTexture 填色＋粗體白字）；幾何一律取 LabActionButtonLayout。
        private void DrawActionButtons(float unit)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_actionLabel == null)
            {
                _actionLabel = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _actionLabel.normal.textColor = Color.white;
            }
            int charges = _hero.Mover.CurrentCharges;
            Rect attack = ToGuiRect(_actionLayout.Attack);
            Rect dash = ToGuiRect(_actionLayout.Dash);
            Fill(attack, AttackColor);
            GUI.Label(attack, "ATK", _actionLabel);
            Fill(dash, charges > 0 ? DashColor : EmptyColor);
            GUI.Label(dash, DashLabels[Mathf.Clamp(charges, 0, DashLabels.Length - 1)], _actionLabel);
            // 準星：螢幕中心＝鏡頭前方。
            float s = 10f * unit;
            Fill(new Rect(_width * 0.5f - s, _height * 0.5f - 1f, s * 2f, 2f), Color.white);
            Fill(new Rect(_width * 0.5f - 1f, _height * 0.5f - s, 2f, s * 2f), Color.white);
        }

        private Rect ToGuiRect(ScreenRegion region)
        {
            return new Rect(region.XMin, _height - region.YMax, region.XMax - region.XMin, region.YMax - region.YMin);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
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
            GUI.Box(_buttons[0], IsThirdPerson ? "TOP" : "THIRD", _buttonStyle);
            GUI.Box(_buttons[1], "LEFT", _buttonStyle);
            GUI.Box(_buttons[2], "RIGHT", _buttonStyle);
            GUI.Box(_buttons[3], "RESET", _buttonStyle);
            if (IsThirdPerson)
            {
                TouchGestureRouter r = _input.ContinuousRouter;
                float unit = Mathf.Min(_width / 844f, _height / 390f);
                Vector2 origin = r.MoveHeld ? new Vector2(r.MoveOriginX, _height - r.MoveOriginY) : new Vector2(92f * unit, _height - 90f * unit);
                float radius = r.JoystickRadiusPixels;
                GUI.Box(new Rect(origin.x - radius, origin.y - radius, radius * 2f, radius * 2f), GUIContent.none, _buttonStyle);
                GUI.Box(new Rect(origin.x + r.MoveX * radius - 14f, origin.y - r.MoveY * radius - 14f, 28f, 28f), GUIContent.none);
                if (r.ActionButtonsEnabled) DrawActionButtons(unit);
            }
            GUI.Label(_help, IsThirdPerson ? "Left: move | Right: look | ATK/DASH/Rune aim +" : _panel.width >= 300f ? "Tap ground: move | enemy: attack" : "Tap ground / enemy", _labelStyle);
        }
    }
}
