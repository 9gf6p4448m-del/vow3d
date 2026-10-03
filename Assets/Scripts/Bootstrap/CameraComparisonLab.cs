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
        // 試玩回饋：準星在螢幕正中央、正好疊在英雄身上。鏡頭位置不動（仍繞英雄軌道），視線抬高這個角度，
        // 英雄下移到畫面下半部、準星落在他頭頂上方；ATK／DASH／塑牆只用水平朝向，不受影響。
        public const float LookUpBiasDegrees = 12f;
        private const float CollisionRadius = 0.25f;
        private readonly RaycastHit[] _hits = new RaycastHit[128];
        private readonly Rect[] _buttons = new Rect[4];
        // ── 第三人稱戰鬥操作（GDD §貳.4 模式 C）──
        private static readonly string[] DashLabels = { "DASH 0", "DASH 1", "DASH 2", "DASH 3", "DASH 4" };
        private static readonly Color AttackColor = new Color(0.85f, 0.45f, 0.2f, 0.9f);
        private static readonly Color DashColor = new Color(0.3f, 0.7f, 0.45f, 0.9f);
        // v0.16.0 武器灰盒（GDD §貳.4 模式 C）：WPN 鈕循環切換，標籤＝目前武器（依 WeaponId 序）。
        private static readonly string[] WeaponLabels = { "STD", "SWORD", "BOW", "HAMMER", "HOOK" };
        private static readonly Color WeaponColor = new Color(0.45f, 0.4f, 0.75f, 0.9f);
        private WeaponSelection _weapon;
        public WeaponId CurrentWeapon => _weapon.CurrentId;
        public int WeaponSwitchCount { get; private set; }
        // 錘：獨立橫掃流程（冷卻＋前搖後結算扇形傷害），不經 HeroCombatBrain 的單目標狀態機。
        private WeaponSweepTimer _sweep;
        private float _sweepDirX, _sweepDirZ;
        public int SweepStartCount { get; private set; }
        public int SweepResolveCount { get; private set; }
        public int LastSweepHits { get; private set; }
        // 鉤鎖（v0.17.0）：拉自己到目標前 2m，逐幀走 HeroLocomotion.ApplyDisplacement（對牆裁切、縛足歸零）；抵達才接既有普攻。
        private GrappleHook _grapple;
        private ICombatTarget _grappleTarget;
        private Vector3 _grappleShortfall;   // 整段累計「要求位移－實際位移」（水平）
        private const float GrappleBlockedMeters = 0.3f;   // 累計落差超過即算被擋（同 G2 停點容差）
        // 弓蓄力＋按住 ATK 範圍預覽（2026-10-03，vow-toolchain/acceptance-bowcharge-20261003.md）：
        // 弓按下只開始蓄力，放開才朝準星出手（錐內沒人也算一次出手）；其他武器仍按下即出手，按住只多一個範圍預覽。
        // 蓄力中 DASH／切 WPN／切 TOP↔THIRD／模式切換／觸控 Canceled／輸入被鎖 → 作廢，不出手。
        private bool _attackHeld;
        private double _attackPressedAt;   // Time.unscaledTimeAsDouble，與觸控路由同一個時鐘
        public int BowShotCount { get; private set; }
        public WeaponAimPreview ActivePreview { get; private set; }
        public bool IsAttackHeld => _attackHeld;
        private const int PreviewArcSegments = 32;
        private const float PreviewGroundOffset = 0.05f;
        private static readonly Color PreviewColor = new Color(0.55f, 0.9f, 1f, 0.9f);
        private readonly Vector3[] _previewPoints = new Vector3[PreviewArcSegments + 3];
        private GameObject _previewObject;
        private LineRenderer _previewLine;
        public GameObject AimPreviewIndicator => _previewObject;
        // 追加 A11（使用者 2026-10-03 裁定 M1 選 A「蓄力時停火」）：弓按住期間不自動普攻。
        // _bowHoldTarget＝按下時正在打的目標：按住中當黏性偏好（快速點擊與現行一致）、取消後恢復自動普攻、放開沒挑到目標時接回。
        private ICombatTarget _bowHoldTarget;
        private bool _bowResumePending;
        // 手勢操作第一批（2026-10-03，vow-toolchain/acceptance-bowaim-20261003.md）：弓按住 ATK 時左右拖曳＝調整出手方向。
        // 按下記 pressYaw＝當下準星 yaw；拖曳只改 offset（BowAimLogic，名目 mm）；出手、標記、預覽錐一律朝 aimYaw＝pressYaw＋offset。
        // 按住期間鏡頭以最大 90°/s 朝 aimYaw 追；放開／取消就停在當下（不回彈）。其他武器收到拖曳一律不理。
        private float _bowPressYaw, _bowAimOffset;
        private const float MaxCameraTrackStepSeconds = 0.1f;   // 卡頓一幀最多轉 9°，不因長幀瞬間跳轉
        private bool BowAiming => _attackHeld && _weapon.CurrentId == WeaponId.Bow;
        private float BowAimYaw => BowAimLogic.NormalizeDegrees(_bowPressYaw + _bowAimOffset);
        private static readonly Color FallbackMarkerColor = new Color(1f, 0.92f, 0.5f, 0.85f);
        private const float SightHeight = 1.0f;
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private const float MarkerHeight = 3.0f;   // 頭頂血條在 2.4m，標記放在它上面
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
            CreatePreviewIndicator();
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
            _input.OnLabActionButtonReleased += OnActionButtonReleased;
            _input.OnLabActionButtonCanceled += OnActionButtonCanceled;
            _input.OnLabActionButtonDragged += OnActionButtonDragged;
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
                    _input.OnLabActionButtonReleased -= OnActionButtonReleased;
                    _input.OnLabActionButtonCanceled -= OnActionButtonCanceled;
                    _input.OnLabActionButtonDragged -= OnActionButtonDragged;
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
                ApplyWeaponRange();
                if (_runeCaster != null) _runeCaster.SetDragDirectionOverride(_aimGround);
                if (_runeGhost != null) _runeGhost.SetDragDirectionOverride(_aimGround);
                if (_hud != null) _hud.SetCameraLabThirdPerson(true);
                RefreshLayout();
                PositionThirdPerson();
            }
            else
            {
                IsThirdPerson = false;
                CancelAttackHold();
                _hero.DisarmChargedShot();   // 俯視不結算已放開、尚未命中的蓄力箭（同錘的 CancelPending）
                _input.ContinuousRouter.SetThirdPersonEnabled(false);
                _hero.SetContinuousMoveSource(null);
                ApplyWeaponRange();
                _sweep.CancelPending();   // 俯視不結算未完成的橫掃；冷卻照算（覆審 r1 L1）
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
            // 覆審 r1 L2：前搖到點時武器已不是錘、或輸入被鎖（倒地／通風口飛行／對局暫停）→取消這一掃。
            if (_ready && IsThirdPerson && _sweep.TryConsumeResolve(Time.time) && _weapon.Current.IsSweep && InputPermitted) ResolveSweep();
            if (_ready && _grapple.Pulling) StepGrapple();
            // 覆審 r3 N1：第三人稱拿錘＝不留單目標普攻。不論目標從哪來（點敵人、冷卻中 TOP 鎖定後切回、切武器、起手），
            // 只要英雄能移動且不在滑步中就原地清掉；滑步／收招中等到可移動那一幀才清，不排入「走回頭」的待執行移動（F2）。
            if (_ready && IsThirdPerson && _weapon.Current.IsSweep) ClearTargetForSweepWeapon();
            // 追加 A11：弓蓄力中停火——不留普攻目標（前搖可打斷時原地清掉、不命中；後搖中等到可移動那一幀）。
            if (_ready && IsThirdPerson && _attackHeld && _weapon.CurrentId == WeaponId.Bow
                && BowChargeLogic.IsCharging(Time.unscaledTimeAsDouble - _attackPressedAt)) ClearTargetForSweepWeapon();
            else if (_ready && _bowResumePending) ResumeBowHoldTarget();
        }

        private void LateUpdate()
        {
            if (!_ready || !IsThirdPerson)
            {
                PreviewTarget = null;
                PreviewInCone = false;
                if (_attackHeld) EndAttackHold();
                return;
            }
            TouchGestureRouter router = _input.ContinuousRouter;
            if (!InputPermitted) router.CancelContinuousTouches();
            float sensitivity = 2.5f / Mathf.Clamp(_input.PixelsPerMillimeter, 3f, 25f);
            _yaw = Mathf.Repeat(_yaw + router.LookDeltaX * sensitivity, 360f);
            _pitch = Mathf.Clamp(_pitch - router.LookDeltaY * sensitivity, 10f, 50f);
            router.ConsumeLook();
            if (BowAiming && InputPermitted)
                _yaw = BowAimLogic.StepYawToward(_yaw, BowAimYaw, BowAimLogic.CameraTrackDegreesPerSecond,
                    Mathf.Min(Time.unscaledDeltaTime, MaxCameraTrackStepSeconds));
            PositionThirdPerson();
            RefreshAimPreview();
            RefreshWeaponPreview();
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
            _rig.transform.SetPositionAndRotation(focus + direction * distance,
                Quaternion.Euler(_pitch - LookUpBiasDegrees, _yaw, 0f));
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
            // 放不下時的提示標籤：避開首頁底部置中的 #vow-version 版本列（bottom 6px＋兩行約 31 CSS px）。
            // WebGL 的 Screen.dpi＝96×devicePixelRatio，用它把 40 CSS px 換成裝置像素（審查 r1 F4）。
            float cssPixel = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 96f) : 1f;
            if (!_layoutAvailable) _panel = new Rect(_width / 2f - 100f, _height - 38f - 40f * cssPixel, 200f, 24f);
            _title = new Rect(x + 4f, y + 2f, width - 8f, 22f);
            _help = new Rect(x + 4f, y + height - 23f, width - 8f, 22f);
            int columns = oneRow ? 4 : 2;
            float buttonWidth = (width - 10f - (columns - 1) * 4f) / columns;
            for (int i = 0; i < 4; i++)
                _buttons[i] = new Rect(x + 5f + (i % columns) * (buttonWidth + 4f),
                    y + 26f + (i / columns) * 46f, buttonWidth, 42f);
            // 俯視（TOP）只留 TOP/THIRD 切換鈕：LEFT/RIGHT/RESET 只在第三人稱有作用，TOP 不畫、不佔 UI 區、不吃點擊（審查 r1 F4/F6）。
            // 切換鈕位置與大小不變；面板區縮成就是這顆鈕。
            if (!IsThirdPerson && _layoutAvailable) _panel = _buttons[0];
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
            _actionLayout = LabActionButtonLayout.Compute(_width, _height, _input.PixelsPerMillimeter, _talentVisible);
            _input.ContinuousRouter.ActionButtons = _actionLayout;
            _input.ContinuousRouter.ActionButtonsEnabled = IsThirdPerson && _layoutAvailable && enabled;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 驗證用（Playwright 換算觸控座標）：只在版面重算時輸出，Unity 螢幕座標（原點左下）。
            RuneButtonLayout runeLog = RuneButtonLayout.Compute(_width, _height, _input.PixelsPerMillimeter);
            Debug.Log("[CAMERA LAB] LAYOUT third=" + IsThirdPerson + " w=" + _width + " h=" + _height
                + " toggle=" + Pt(_buttons[0].center.x, _height - _buttons[0].center.y)
                + " atk=" + Pt((_actionLayout.Attack.XMin + _actionLayout.Attack.XMax) * .5f, (_actionLayout.Attack.YMin + _actionLayout.Attack.YMax) * .5f)
                + " dash=" + Pt((_actionLayout.Dash.XMin + _actionLayout.Dash.XMax) * .5f, (_actionLayout.Dash.YMin + _actionLayout.Dash.YMax) * .5f)
                + " wpn=" + Pt((_actionLayout.Weapon.XMin + _actionLayout.Weapon.XMax) * .5f, (_actionLayout.Weapon.YMin + _actionLayout.Weapon.YMax) * .5f)
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
            if (button == LabActionButton.Attack)
            {
                _attackHeld = true;
                _attackPressedAt = Time.unscaledTimeAsDouble;
                _bowPressYaw = _yaw;   // 每次按下都重新起算：offset 歸零、pressYaw＝當下準星
                _bowAimOffset = 0f;
                if (_weapon.Current.IsSweep) HammerSweep();
                else if (_weapon.Current.IsGrapple) GrappleAttack();
                else if (_weapon.CurrentId == WeaponId.Bow)   // 弓：按下只開始蓄力，放開才出手（OnActionButtonReleased）
                {
                    // 覆審 r3 MEDIUM-1：取消後還在等接回（滑步中）又按住→目標已被停火清掉，沿用原目標，不覆寫成 null。
                    ICombatTarget current = _hero.CurrentTarget;
                    if (current != null || !_bowResumePending) _bowHoldTarget = current;
                    _bowResumePending = false;
                }
                else AimAttack(_yaw);
                RefreshWeaponPreview();
            }
            else if (button == LabActionButton.Dash) { CancelAttackHold(); if (!_grapple.Pulling) ActiveDash(); }
            else if (button == LabActionButton.Weapon) { CancelAttackHold(); CycleWeapon(); }
        }

        // ATK 放開：弓才在這裡出手；其他武器只收掉預覽。作廢過（_attackHeld 已清）的放開一律不理。
        private void OnActionButtonReleased(LabActionButton button, float heldSeconds)
        {
            if (button != LabActionButton.Attack || !_attackHeld) return;
            bool bow = _weapon.CurrentId == WeaponId.Bow;
            float aimYaw = BowAimYaw;
            EndAttackHold();
            if (!bow || !_ready || !IsThirdPerson || !InputPermitted) { _bowHoldTarget = null; return; }
            BowRelease(heldSeconds, aimYaw);
            // 追加 A11：放開沒挑到目標（錐內無人）＝按住前在打的目標接回自動普攻（同現行弓「錐內無人不改目標」）。
            if (LastAimTarget == null && _bowHoldTarget != null) _bowResumePending = true;
            else _bowHoldTarget = null;
        }

        private void OnActionButtonCanceled(LabActionButton button)
        {
            if (button == LabActionButton.Attack) CancelAttackHold();
        }

        // 從 ATK 起手的拖曳：只有弓、而且按住中才吃（水平位移→出手偏角；垂直本批不用）。作廢後的拖曳一律不理。
        private void OnActionButtonDragged(LabActionButton button, float dxMillimeters, float dyMillimeters)
        {
            if (button != LabActionButton.Attack || !BowAiming || !_ready || !IsThirdPerson) return;
            _bowAimOffset = BowAimLogic.OffsetDegrees(dxMillimeters);
        }

        // 作廢按住（DASH／WPN／切 TOP／觸控 Canceled）：弓的話之後恢復自動普攻（追加 A11b）。
        private void CancelAttackHold()
        {
            bool bowHold = _attackHeld && _weapon.CurrentId == WeaponId.Bow;
            EndAttackHold();
            if (bowHold && _bowHoldTarget != null) _bowResumePending = true;
        }

        // 恢復按住前的自動普攻：等英雄可移動、不在滑步中；玩家已另選目標、推著搖桿、換成錘或輸入被鎖就放棄。
        private void ResumeBowHoldTarget()
        {
            if (_attackHeld) return;
            if (!InputPermitted || _weapon.Current.IsSweep || _input.ContinuousRouter.MoveHeld || _hero.CurrentTarget != null)
            {
                _bowResumePending = false;
                _bowHoldTarget = null;
                return;
            }
            if (!_hero.StateMachine.CanMove || _hero.Mover.IsDashing) return;
            ICombatTarget target = _bowHoldTarget;
            _bowResumePending = false;
            _bowHoldTarget = null;
            if (target != null && target.IsAlive) _input.SubmitCombatTarget(target);
        }

        private void EndAttackHold()
        {
            _attackHeld = false;
            HidePreview();
        }

        // 弓放開：t < 0.2s＝快速射擊，與 05f97b9 的按下即出手同一條 AimAttack（錐 12°、12m、倍率 1.0、不穿透）；
        // 其餘依 BowChargeLogic 收窄錐、延伸射程、加倍率（滿蓄穿透），交給英雄下一次對該目標的普攻結算。錐內沒人也算一次出手。
        private void BowRelease(double heldSeconds, float aimYaw)
        {
            BowShotCount++;
            BowShot shot = BowChargeLogic.Resolve(heldSeconds);
            // 追加 A12b：快速射擊不作廢還沒命中的蓄力箭——同一目標重送＝無事發生；挑到別的目標時由英雄的換目標入口解除。
            if (shot.IsQuick)
            {
                AimAttack(aimYaw);   // 手勢第一批：快速射擊同樣朝 aimYaw
                return;
            }
            _hero.DisarmChargedShot();   // 新的蓄力箭取代上一支還沒命中的：一次蓄力只算一支
            AimAttackCount++;
            LastAimTarget = null;
            CombatTargetRoster roster = _bootstrap.ElementRoster;
            if (roster == null) return;
            int picked = ResolveAimTarget(roster, out AimTargetPicker _, aimYaw, shot.ConeHalfAngleDegrees, shot.RangeMeters);
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CAMERA LAB] BOW held=" + heldSeconds.ToString("F2") + " p=" + shot.Progress.ToString("F2")
                + " target=" + (picked >= 0 ? roster.GetBehaviour(picked).name : "none"));
#endif
            if (picked < 0) return;
            ICombatTarget target = roster.Get(picked);
            LastAimTarget = target;
            _hero.ArmChargedShot(target, shot.RangeMeters, shot.DamageMultiplier, shot.Pierce, _bootstrap.TargetRegistry);
            _input.SubmitCombatTarget(target);
        }

        private void CycleWeapon()
        {
            _hero.DisarmChargedShot();   // 離開弓：已放開、尚未命中的蓄力箭不再結算
            _weapon.Next();
            // 覆審 r1 M1：切到錘（不走單目標普攻）時原地清掉普攻目標（循環順序下離開弓必定切到錘）。
            if (IsThirdPerson && _weapon.Current.IsSweep) ClearTargetForSweepWeapon();
            WeaponSwitchCount++;
            ApplyWeaponRange();
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CAMERA LAB] WPN weapon=" + WeaponLabels[(int)_weapon.CurrentId] + " atkRange=" + _hero.AttackRangeOverride.ToString("F1"));
#endif
        }

        // 錘：按 ATK 就朝準星水平前方起手（錐內沒人也出手，使用者 2026-10-02 簽准）；冷卻內再按不起手。
        private void HammerSweep()
        {
            AimAttackCount++;
            LastAimTarget = null;
            WeaponSpec hammer = _weapon.Current;
            if (!_sweep.TryStart(Time.time, hammer.SweepCooldownSeconds, hammer.SweepWindupSeconds)) return;
            SweepStartCount++;
            // 覆審 r2 F1：以起手為準清掉普攻目標——不論目標從哪個入口來（含 TOP 鎖定後切回 THIRD），錘下都不疊普攻。
            ClearTargetForSweepWeapon();
            CameraLabAim.GroundForward(_yaw, out _sweepDirX, out _sweepDirZ);
        }

        // 前搖結束：以英雄當下位置為頂點、起手時的準星方向為軸，扇形內每個可傷目標各吃一次 AttackDamage。
        private void ResolveSweep()
        {
            SweepResolveCount++;
            LastSweepHits = 0;
            CombatTargetRoster roster = _bootstrap.ElementRoster;
            if (roster == null || !_hero.IsAlive) return;
            WeaponSpec hammer = WeaponSpec.Hammer;
            Vector3 apex = _hero.transform.position;
            float damage = _hero.AttackDamage;
            Faction faction = _hero.HeroFaction;
            for (int i = 0; i < roster.Count; i++)
            {
                CombatTargetBehaviour target = roster.GetBehaviour(i);
                if (!IsSweepDamageable(target, faction)) continue;
                Vector3 p = target.TargetTransform.position;
                if (!WeaponSweep.Contains(hammer, apex.x, apex.z, _sweepDirX, _sweepDirZ, p.x, p.z)) continue;
                target.ReceiveDamage(damage, DamageType.Physical, _hero.gameObject);
                LastSweepHits++;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CAMERA LAB] SWEEP hits=" + LastSweepHits + " dir=(" + _sweepDirX.ToString("F3") + "," + _sweepDirZ.ToString("F3") + ")");
#endif
        }

        // 鉤鎖：挑準星錐內（±20°、10m、視線無石牆、同一樓地板）的目標，把自己拉到它前方 2m。鉤本身不傷害。
        // 使用者 2026-10-02 補充裁定：目標已在普攻射程內（EffectiveAttackRange）→直接普攻，不起鉤、不吃冷卻、不打斷前搖（M3）；
        // 冷卻中→退回普通普攻：射程內才打，射程外原地不動、不追（M2）。縛足、不能移動（後搖／滑步中）時不起鉤（不吃冷卻）。
        private void GrappleAttack()
        {
            AimAttackCount++;
            LastAimTarget = null;
            if (_grapple.Pulling) return;
            CombatTargetRoster roster = _bootstrap.ElementRoster;
            if (roster == null) return;
            int picked = ResolveAimTarget(roster, out AimTargetPicker _, _yaw);
            if (picked < 0) return;
            ICombatTarget target = roster.Get(picked);
            bool inAttackRange = _hero.IsTargetInAttackRange(target);
            if (inAttackRange || !_grapple.IsReady(Time.time))
            {
                if (!inAttackRange) return;
                LastAimTarget = target;
                _input.SubmitCombatTarget(target);   // 同一目標重送＝無事發生（HeroCombatBrain），不打斷前搖
                return;
            }
            if (_hero.IsRooted || !_hero.StateMachine.CanMove || _hero.Mover.IsDashing) return;
            Vector3 h = _hero.transform.position, p = target.TargetTransform.position;
            if (!_grapple.TryStart(Time.time, _weapon.Current, h.x, h.z, p.x, p.z)) return;
            LastAimTarget = target;
            _grappleTarget = target;
            _grappleShortfall = Vector3.zero;
            _hero.ClearCombatTargetInPlace();   // 拉的途中不疊普攻追擊／導航
        }

        // 每幀一段位移；被牆擋住或縛足（整段累計實際位移比要求短少超過 0.3m，與幀率無關）→停在原地、不接普攻。
        // 切武器／輸入被鎖（含倒地）／回俯視→取消。冷卻照算。
        private void StepGrapple()
        {
            if (!IsThirdPerson || !InputPermitted || !_weapon.Current.IsGrapple)
            {
                _grapple.Cancel();
                _grappleTarget = null;
                return;
            }
            bool done = _grapple.Step(Time.deltaTime, out float dx, out float dz);
            Vector3 want = new Vector3(dx, 0f, dz);
            Vector3 moved = _hero.GetComponent<HeroLocomotion>().ApplyDisplacement(want);
            moved.y = 0f;
            _grappleShortfall += want - moved;
            if (_grappleShortfall.sqrMagnitude > GrappleBlockedMeters * GrappleBlockedMeters)
            {
                _grapple.Cancel();
                _grappleTarget = null;
                return;
            }
            if (!done) return;
            ICombatTarget target = _grappleTarget;
            _grappleTarget = null;
            if (target != null && target.IsAlive) _input.SubmitCombatTarget(target);
        }

        // 與 ElementField.IsElementDamageable 同語意：己方石牆不吃、其餘依 CanBeTargetedBy（同陣營不傷）。
        private static bool IsSweepDamageable(CombatTargetBehaviour target, Faction heroFaction)
        {
            if (target == null || !target.IsAlive || target.TargetTransform == null) return false;
            if (target.TargetFaction == Faction.DestructibleWall && target.OwnerFaction == heroFaction) return false;
            return target.CanBeTargetedBy(heroFaction);
        }

        // 錘下清普攻目標的唯一入口：只在有目標、可移動（Idle／Moving／前搖可打斷）且不在滑步中時下原地移動指令。
        private void ClearTargetForSweepWeapon()
        {
            if (_hero.CurrentTarget == null || !_hero.StateMachine.CanMove || _hero.Mover.IsDashing) return;
            _hero.ClearCombatTargetInPlace();
        }

        // 武器射程覆寫只在第三人稱生效（弓 12m）；俯視與其他武器一律清掉＝沿用 HeroTuningAsset。
        private void ApplyWeaponRange()
        {
            WeaponSpec weapon = _weapon.Current;
            _hero.SetAttackRangeOverride(IsThirdPerson && weapon.OverridesAttackRange ? weapon.AttackRangeMeters : 0f);
        }

        // ATK 與按前預覽共用同一個挑選：標記畫在哪，按下去就打誰。
        // preferred＝英雄正在打的目標（黏性，見 AimTargetPicker）。
        // coneHalfAngleOverride／aimRangeOverride ≥ 0：弓蓄力改寫錐半角與距離（< 0＝沿用武器，原路徑不變）。
        // yawDegrees＝準星方向：一般＝鏡頭 _yaw；弓按住拖曳時＝aimYaw（手勢第一批）。
        private int ResolveAimTarget(CombatTargetRoster roster, out AimTargetPicker picker, float yawDegrees,
            float coneHalfAngleOverride = -1f, float aimRangeOverride = -1f)
        {
            Vector3 origin = _hero.transform.position;
            CameraLabAim.GroundForward(yawDegrees, out float ax, out float az);
            picker = default;
            WeaponSpec weapon = _weapon.Current;   // Standard＝CameraLabAim 常數，行為同 v0.15
            if (coneHalfAngleOverride >= 0f) picker.Begin(origin.x, origin.z, ax, az, weapon, coneHalfAngleOverride, aimRangeOverride);
            else picker.Begin(origin.x, origin.z, ax, az, weapon);
            float aimRange = aimRangeOverride >= 0f ? aimRangeOverride : weapon.AimRangeMeters;
            ICombatTarget current = _hero.CurrentTarget;
            if (current == null && weapon.Id == WeaponId.Bow) current = _bowHoldTarget;   // 追加 A11：弓按住中已停火，黏性仍以按下前在打的目標為準（只限弓，覆審 r3 LOW-1）
            for (int i = 0; i < roster.Count; i++)
            {
                CombatTargetBehaviour candidate = roster.GetBehaviour(i);
                if (candidate == null || !candidate.IsAlive || candidate.TargetTransform == null) continue;
                // 與點擊同規則：己方石牆不當目標（點擊會穿過去）；其餘交給英雄唯一的交戰判準。
                if (candidate.TargetFaction == Faction.DestructibleWall && candidate.OwnerFaction == _hero.HeroFaction) continue;
                if (!HasEnabledCollider(candidate)) continue;   // 佔領模式停用的木樁／測試牆：看不見、點不到→也挑不到
                if (!_hero.CanEngage(candidate)) continue;
                Vector3 p = candidate.TargetTransform.position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                bool inRange = dx * dx + dz * dz <= aimRange * aimRange;
                // 鉤鎖：錐內也要視線無石牆、同一樓地板（不穿牆、不跨崖）；預覽與按下共用。
                if (weapon.IsGrapple && (!HasClearSight(candidate) || !SameFloor(origin, p))) continue;
                // 只影響「錐外退回最近者」：沒瞄、系統自己挑時，只挑按下去馬上有結果的目標——
                // 不在石牆後（英雄會突然跑去繞牆），且已在射程內或同一樓地板走得到（崖台→谷底超出射程會走一步就放棄）。
                bool eligible = inRange && HasClearSight(candidate) && (_hero.IsTargetInAttackRange(candidate) || SameFloor(origin, p));
                picker.Consider(i, p.x, p.z, ReferenceEquals(candidate, current), eligible);
            }
            // 錐內優先；錐內沒有就退回 8m 內最近者，8m 內都沒有才不出手（Standard；其他武器見 WeaponSpec）。
            return picker.ResolvedIndex;
        }

        // 正式版「同一樓地板」判準（V0140 §5.2）；平地恆真。
        private bool SameFloor(Vector3 a, Vector3 b)
        {
            ITerrainQuery terrain = _hero.GetComponent<HeroLocomotion>().TerrainQuery;
            if (terrain == null) return true;
            return terrain.IsSameFloor(a.x, a.z, terrain.ResolveLayer(a.x, a.z, a.y), b.x, b.z, terrain.ResolveLayer(b.x, b.z, b.y));
        }

        private static bool HasEnabledCollider(CombatTargetBehaviour target)
        {
            Collider[] colliders = target.TargetColliders;
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled) return true;
            return false;
        }

        // 英雄身高 1m → 目標身高 1m 的直線上，有石牆（不分陣營）就算被擋；地形、其他場景物件與其他敵人／木樁不擋。
        // 地形視野（谷底看不到崖台等）由 CanEngage 負責；崖壁若也擋，崖台往谷底會因掠過崖緣幾公分而時靈時不靈（H 修訂）。
        // 走 RaycastNonAlloc＋TryGetComponent，零配置（Editor 下 GetComponent 找不到會配置假 null 物件）。
        private bool HasClearSight(CombatTargetBehaviour target)
        {
            Vector3 from = _hero.transform.position + Vector3.up * SightHeight;
            Vector3 to = target.transform.position + Vector3.up * SightHeight;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, _sightHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == _sightHits.Length) return false;   // 緩衝滿時不猜：保守當被擋
            for (int i = 0; i < count; i++)
            {
                Transform t = _sightHits[i].collider.transform;
                if (t.IsChildOf(_hero.transform) || t.IsChildOf(target.transform)) continue;
                CombatTargetBehaviour owner = null;
                while (t != null && !t.TryGetComponent(out owner)) t = t.parent;
                if (owner != null && owner.TargetFaction == Faction.DestructibleWall) return false;
            }
            return true;
        }

        private void AimAttack(float yawDegrees)
        {
            AimAttackCount++;
            LastAimTarget = null;
            CombatTargetRoster roster = _bootstrap.ElementRoster;
            if (roster == null) return;
            int picked = ResolveAimTarget(roster, out AimTargetPicker picker, yawDegrees);
            if (picked < 0) return;
            LastAimTarget = roster.Get(picked);
            _input.SubmitCombatTarget(LastAimTarget);
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CAMERA LAB] ATK target=" + roster.GetBehaviour(picked).name + " cone=" + (picker.BestIndex >= 0));
#endif
        }

        // 按下 ATK 會打誰（第三人稱且按鈕啟用才有值）。每個 LateUpdate 重算，零配置。
        public ICombatTarget PreviewTarget { get; private set; }
        public bool PreviewInCone { get; private set; }

        private void RefreshAimPreview()
        {
            PreviewTarget = null;
            PreviewInCone = false;
            if (!IsThirdPerson || !ActionButtonsActive || !InputPermitted) return;
            CombatTargetRoster roster = _bootstrap.ElementRoster;
            if (roster == null) return;
            int picked;
            AimTargetPicker picker;
            // 覆審 r1 M2：弓蓄力中，標記用蓄力後的錐／射程挑（＝現在放開會打的目標）。
            if (BowAiming)
            {
                BowShot shot = BowChargeLogic.Resolve(Time.unscaledTimeAsDouble - _attackPressedAt);
                picked = ResolveAimTarget(roster, out picker, BowAimYaw, shot.ConeHalfAngleDegrees, shot.RangeMeters);
            }
            else picked = ResolveAimTarget(roster, out picker, _yaw);
            if (picked < 0) return;
            PreviewTarget = roster.Get(picked);
            PreviewInCone = picker.BestIndex >= 0;
        }

        // 按住 ATK 的範圍預覽：形狀＋參數＝WeaponAimPreview（Standard 無）；頂點＝英雄腳下、方向＝準星水平前方。每幀零配置。
        private void RefreshWeaponPreview()
        {
            if (!_attackHeld) return;
            if (!IsThirdPerson || !InputPermitted)
            {
                EndAttackHold();
                _bowHoldTarget = null;   // 覆審 r3 LOW-1：輸入被鎖（倒地／通風口）結束的按住不接回，也不留給其他武器當黏性
                _bowResumePending = false;
                return;
            }
            WeaponAimPreview preview = WeaponAimPreview.For(_weapon.CurrentId, Time.unscaledTimeAsDouble - _attackPressedAt);
            ActivePreview = preview;
            if (preview.Kind == WeaponPreviewKind.None || _previewLine == null)
            {
                if (_previewObject != null && _previewObject.activeSelf) _previewObject.SetActive(false);
                return;
            }
            Vector3 apex = _hero.transform.position;
            apex.y += PreviewGroundOffset;
            CameraLabAim.GroundForward(BowAiming ? BowAimYaw : _yaw, out float ax, out float az);   // 弓：預覽錐跟手指（aimYaw），不跟鏡頭
            float baseDegrees = Mathf.Atan2(ax, az) * Mathf.Rad2Deg;
            float full = preview.FullAngleDegrees;
            float range = preview.RangeMeters;
            _previewPoints[0] = apex;
            for (int i = 0; i <= PreviewArcSegments; i++)
            {
                float radians = (baseDegrees - preview.HalfAngleDegrees + full * i / PreviewArcSegments) * Mathf.Deg2Rad;
                _previewPoints[i + 1] = new Vector3(apex.x + Mathf.Sin(radians) * range, apex.y, apex.z + Mathf.Cos(radians) * range);
            }
            _previewPoints[_previewPoints.Length - 1] = apex;
            _previewLine.SetPositions(_previewPoints);
            if (!_previewObject.activeSelf) _previewObject.SetActive(true);
        }

        private void HidePreview()
        {
            ActivePreview = default;
            if (_previewObject != null && _previewObject.activeSelf) _previewObject.SetActive(false);
        }

        // 一條 LineRenderer（設定比照 VOWPhase1SceneBuilder.CreateSectorTelegraph，借用場景扇形預警的材質）。
        // 不直接重用 SectorTelegraph：它的高度取地形第 0 層，英雄站在崖台上時預覽會埋進地形裡。
        private void CreatePreviewIndicator()
        {
            _previewObject = new GameObject("WeaponAimPreview");
            _previewObject.layer = 2;   // Ignore Raycast
            _previewObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 線寬攤平在地面上
            _previewLine = _previewObject.AddComponent<LineRenderer>();
            _previewLine.useWorldSpace = true;
            _previewLine.loop = false;
            _previewLine.alignment = LineAlignment.TransformZ;
            _previewLine.numCapVertices = 0;
            _previewLine.widthMultiplier = 0.12f;
            _previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _previewLine.receiveShadows = false;
            _previewLine.startColor = PreviewColor;
            _previewLine.endColor = PreviewColor;
            _previewLine.positionCount = _previewPoints.Length;
            Vow.Combat.Feedback.SectorTelegraph sector = _bootstrap.SectorTelegraph;
            LineRenderer source = sector != null ? sector.GetComponent<LineRenderer>() : null;
            if (source != null) _previewLine.sharedMaterial = source.sharedMaterial;
            _previewObject.SetActive(false);
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
            if (!IsThirdPerson && index != 0) return false;   // TOP 只有切換鈕
            point = new Vector2(_buttons[index].center.x, _height - _buttons[index].center.y);
            return true;
        }

        private void OnUiTapped(int id)
        {
            if (id != _region || !_layoutAvailable) return;
            Vector2 point = _input.UiTapScreenPosition;
            point.y = _height - point.y;
            if (_buttons[0].Contains(point)) SetThirdPerson(!IsThirdPerson);
            else if (!IsThirdPerson) return;
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
            if (_actionLayout.WeaponVisible)
            {
                Rect weaponButton = ToGuiRect(_actionLayout.Weapon);
                Fill(weaponButton, WeaponColor);
                GUI.Label(weaponButton, WeaponLabels[(int)_weapon.CurrentId], _actionLabel);
            }
            // 準星：螢幕中心＝鏡頭前方。
            float s = 10f * unit;
            Fill(new Rect(_width * 0.5f - s, _height * 0.5f - 1f, s * 2f, 2f), Color.white);
            Fill(new Rect(_width * 0.5f - 1f, _height * 0.5f - s, 2f, s * 2f), Color.white);
            DrawAimMarker(unit);
        }

        // 按下 ATK 會打誰：目標頭上的菱形（橘＝準星錐內、淡黃＝錐外退回最近者）。位置與 ATK 共用同一個挑選結果。
        private void DrawAimMarker(float unit)
        {
            ICombatTarget target = PreviewTarget;
            if (target == null || !target.IsAlive || target.TargetTransform == null || _camera == null) return;
            Vector3 screen = _camera.WorldToScreenPoint(target.TargetTransform.position + Vector3.up * MarkerHeight);
            if (screen.z <= 0f) return;
            float size = 16f * unit;
            Vector2 center = new Vector2(screen.x, _height - screen.y);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center);
            Fill(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), PreviewInCone ? AttackColor : FallbackMarkerColor);
            GUI.matrix = previousMatrix;
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
            if (!IsThirdPerson)
            {
                GUI.Box(_buttons[0], "THIRD", _buttonStyle);
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
