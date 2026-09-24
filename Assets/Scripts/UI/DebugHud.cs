using System;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;

namespace Vow.UI
{
    // Day 1 可視化調試 HUD（左上角）：FPS、PlayerState、充能點、操作模式切換、220ms 目押窗口倒數條、Hitbox 開關。
    //
    // 為什麼用 IMGUI：這是調試工具，不是正式 UI。關掉 GUILayout、固定 Rect、字串全部預先快取之後，
    // 每幀不產生 GC，不會污染 Profiler 對「遊戲本體 0 GC」的量測。
    // 按鈕不用 GUI.Button：點擊一律走 InputRoutingManager 的 UI 區域，全專案只有一條輸入路徑，
    // 同時保證「點到 HUD 的手指不會滲透成點地移動」。
    public sealed class DebugHud : MonoBehaviour, IDebugHudPanel
    {
        private const float ReferenceDpi = 160f;
        private const float FpsSampleSeconds = 0.25f;
        private const float Pad = 8f;
        private const float Row = 22f;
        private const float PanelWidth = 250f;
        private const float InfoRows = 10f; // 批 3 多一列 SHIELD

        private static readonly string[] StateNames = Enum.GetNames(typeof(PlayerState));

        // v0.6.1（V061_FEEDBACK_PLAN.md §1）：縛足／減速時 STATE 列改顯示這兩個常數字串。
        private const string RootedStateLabel = "ROOTED";
        private const string SlowedStateLabel = "SLOWED";

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color PipOn = new Color(0.2f, 0.95f, 1f);
        private static readonly Color PipOff = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color WindowColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.45f, 0.9f, 0.85f);
        private static readonly Color ButtonOnColor = new Color(0.2f, 0.75f, 0.35f, 0.9f);
        private static readonly Color ButtonCooldownColor = new Color(0.32f, 0.32f, 0.36f, 0.85f);
        private static readonly Color ElemBlueColor = new Color(0.22f, 0.5f, 0.95f, 0.9f);
        private static readonly Color ElemRedColor = new Color(0.8f, 0.24f, 0.2f, 0.9f);

        private HeroController _hero;
        private PlayerInputService _input;
        private HitboxVisualizer _hitboxes;
        private NetworkLatencySimulator _latency;

        private GUIStyle _label;
        private GUIStyle _buttonLabel;
        private float _scale = 1f;
        private float _panelHeight;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private int _modeRegion = -1;
        private int _hitboxRegion = -1;
        private int _latencyRegion = -1;
        private int _gridRegion = -1;
        private int _enemyWallRegion = -1;
        private int _turretRegion = -1;
        private Rect _modeRect;
        private Rect _hitboxRect;
        private Rect _latencyRect;
        private Rect _gridRect;
        private Rect _enemyWallRect;
        private Rect _turretRect;

        // ── Phase 2 批 4：WATER／FIRE／WIND／ELEM 四顆除錯鈕 ──
        private int _waterRegion = -1;
        private int _fireRegion = -1;
        private int _windRegion = -1;
        private int _elemRegion = -1;
        private Rect _waterRect;
        private Rect _fireRect;
        private Rect _windRect;
        private Rect _elemRect;

        private Action _castWater;
        private Action _castFire;
        private Action _castWind;
        private Action _toggleElementFaction;
        private Func<bool> _elementFactionIsBlue;
        private ElementCastCooldowns _elementCooldowns;
        private Func<float> _opponentHealth;
        private DuelRoundLogic _duelRound;

        public void ConfigureDuel(Func<float> opponentHealth, DuelRoundLogic round)
        {
            _opponentHealth = opponentHealth;
            _duelRound = round;
        }

        // ── v0.8.0：CAPTURE 鈕與右上面板的佔領兩列（V080_CAPTURE_PLAN.md §2.4、E27、E29）──
        // HUD 只依賴 ICaptureMatchView；按鈕的動作走 Phase1Bootstrap 交進來的委派（UI 不得反向依賴 Bootstrap）。
        private ICaptureMatchView _captureView;
        private Action _pressCapture;
        private int _captureRegion = -1;
        private Rect _captureRect;
        private Rect _matchPanelRect;
        private float _matchPanelCaptureHeight = 116f;

        private int _blueScoreShown = -1;
        private int _redScoreShown = -1;
        private string _blueScoreLabel = CaptureHudLabels.Score(0);
        private string _redScoreLabel = CaptureHudLabels.Score(0);
        private int _respawnSecondsShown = -1;
        private string _respawnLabel;

        // 比分／復活字串被重算過幾次（零配置量測的活性，V-C03）。
        public int CaptureScoreLabelRecomputeCount { get; private set; }
        public int CaptureRespawnLabelRecomputeCount { get; private set; }

        public void ConfigureCapture(ICaptureMatchView view, Action pressCapture)
        {
            _captureView = view;
            _pressCapture = pressCapture;
            if (_input != null && _pressCapture != null && _captureRegion < 0)
                _captureRegion = _input.Routing.RegisterUiRegion(default);
            RecalculateLayout();
        }

        public void PressCaptureButton() { if (_pressCapture != null) _pressCapture(); }

        public bool TryGetCaptureButtonScreenPoint(out float x, out float y)
        {
            return TryGetButtonScreenPoint(_pressCapture != null && _captureRegion >= 0, _captureRect, out x, out y);
        }

        private bool InCaptureMode => _captureView != null && _captureView.State != CaptureMatchState.Off;

        private MatchGateDecision CurrentGate()
        {
            CaptureMatchState captureState = _captureView != null ? _captureView.State : CaptureMatchState.Off;
            bool heroAlive = _captureView == null || !_captureView.BlueKnockedOut;
            return MatchGate.Evaluate(_duelRound != null ? _duelRound.State : DuelRoundState.Dormant, captureState, heroAlive);
        }

        // 元素／砲台／敵牆鈕反灰：Off 模式與 v0.7.0 的「DuelState != Dormant」逐列相同（V-A20）。
        private bool ElementsLocked => _duelRound != null && CurrentGate().ElementsLocked;

        public string CaptureButtonLabel => InCaptureMode ? CaptureHudLabels.CaptureButtonLabelOn : CaptureHudLabels.CaptureButtonLabelOff;

        // 右上面板第 0 列。Off 時沿用 v0.7.0 的單挑字串。
        public string MatchStatusLabel
        {
            get
            {
                if (!InCaptureMode)
                {
                    if (_duelRound == null) return null;
                    return _duelRound.State == DuelRoundState.Dormant ? "TAP RED TO START"
                         : _duelRound.State == DuelRoundState.Active ? "DUEL ACTIVE" : "RESETTING";
                }
                switch (_captureView.State)
                {
                    case CaptureMatchState.Active: return CaptureHudLabels.StatusActive;
                    case CaptureMatchState.Ended: return ResultLabel(_captureView.Result, false);
                    default: return _captureView.LastResult == CaptureMatchResult.None
                        ? CaptureHudLabels.StatusLobby : ResultLabel(_captureView.LastResult, true);
                }
            }
        }

        public string CaptureBlueScoreLabel => _blueScoreLabel;
        public string CaptureRedScoreLabel => _redScoreLabel;
        public string CaptureRespawnLabel => _captureView != null && _captureView.State == CaptureMatchState.Active
                                             && _captureView.BlueKnockedOut ? _respawnLabel : null;

        private static string ResultLabel(CaptureMatchResult result, bool last)
        {
            switch (result)
            {
                case CaptureMatchResult.BlueWins: return last ? CaptureHudLabels.StatusLastBlueWins : CaptureHudLabels.StatusBlueWins;
                case CaptureMatchResult.RedWins: return last ? CaptureHudLabels.StatusLastRedWins : CaptureHudLabels.StatusRedWins;
                case CaptureMatchResult.Draw: return last ? CaptureHudLabels.StatusLastDraw : CaptureHudLabels.StatusDraw;
                default: return CaptureHudLabels.StatusLobby;
            }
        }

        // 比分與復活倒數：只在數值真的變了才查表換字串（查預建表，零配置）。
        private void RefreshCaptureLabels()
        {
            if (_captureView == null) return;

            int blue = _captureView.BlueScore;
            if (blue != _blueScoreShown)
            {
                _blueScoreShown = blue;
                _blueScoreLabel = CaptureHudLabels.Score(blue);
                CaptureScoreLabelRecomputeCount++;
            }
            int red = _captureView.RedScore;
            if (red != _redScoreShown)
            {
                _redScoreShown = red;
                _redScoreLabel = CaptureHudLabels.Score(red);
                CaptureScoreLabelRecomputeCount++;
            }

            if (!_captureView.BlueKnockedOut) return;
            float remaining = _captureView.BlueRespawnRemaining;
            int whole = (int)remaining;
            int seconds = remaining > whole ? whole + 1 : whole;
            if (seconds == _respawnSecondsShown && _respawnLabel != null) return;
            _respawnSecondsShown = seconds;
            _respawnLabel = CaptureHudLabels.Respawn(remaining);
            CaptureRespawnLabelRecomputeCount++;
        }

        // 零配置字串表：冷卻標籤一律查表，**不得字串串接**。索引＝ElementCastCooldowns.RemainingLabelIndex
        //（0 ＝可用；1..5 ＝向上取整的剩餘秒），所以**索引就是剩餘秒數**：表要遞增排。
        // 反過來排的話按下當下（index 5）會顯示「WATER 1」，4 秒後（index 1）顯示「WATER 5」——V4-n 守這條。
        private static readonly string[] WaterLabels = { "WATER", "WATER 1", "WATER 2", "WATER 3", "WATER 4", "WATER 5" };
        private static readonly string[] FireLabels = { "FIRE", "FIRE 1", "FIRE 2", "FIRE 3", "FIRE 4", "FIRE 5" };
        private static readonly string[] WindLabels = { "WIND", "WIND 1", "WIND 2", "WIND 3", "WIND 4", "WIND 5" };
        private const string ElemBlueLabel = "ELEM: BLUE";
        private const string ElemRedLabel = "ELEM: RED";

        private int _waterIndexShown = -1;
        private int _fireIndexShown = -1;
        private int _windIndexShown = -1;
        private int _elemBlueShown = -1;
        private string _waterLabel = WaterLabels[0];
        private string _fireLabel = FireLabels[0];
        private string _windLabel = WindLabels[0];
        private string _elemLabel = ElemBlueLabel;

        // 標籤被重算過幾次（V5-e 的活性：證明零配置字串表真的走到）。
        public int ElementLabelRecomputeCount { get; private set; }

        // v0.6.1：STATE 列此刻該顯示什麼（縛足／減速優先，其餘照舊顯示狀態機名稱）。
        // Phase1Bootstrap 對外多開 HudStateLabel 轉交這個值——PlayMode 測試 asmdef 看不到 Vow.UI。
        public string StateLabel
        {
            get
            {
                if (_hero == null) return _stateName;
                int status = ElementCalloutLogic.HeroStatusIndex(_hero.IsRooted, _hero.QuicksandSpeedMultiplier);
                if (status == ElementCalloutLogic.RootedStatus) return RootedStateLabel;
                if (status == ElementCalloutLogic.SlowedStatus) return SlowedStateLabel;
                return _stateName;
            }
        }

        public string WaterButtonLabel => _waterLabel;
        public string FireButtonLabel => _fireLabel;
        public string WindButtonLabel => _windLabel;
        public string ElementFactionButtonLabel => _elemLabel;

        public void PressWaterButton() { if (_castWater != null) _castWater(); }
        public void PressFireButton() { if (_castFire != null) _castFire(); }
        public void PressWindButton() { if (_castWind != null) _castWind(); }
        public void PressElementFactionButton() { if (_toggleElementFaction != null) _toggleElementFaction(); }

        // Phase 2 批 2 的 GRID 除錯疊圖開關。疊圖本體在 Vow.Bootstrap（UI 不得反向依賴 Bootstrap），
        // 所以這裡只收兩個委派，由 Phase1Bootstrap 在組裝時各建一次。
        private Func<bool> _gridVisible;
        private Action _toggleGrid;

        // Phase 2 批 3：敵方牆生成與砲台開關同樣走委派（元件本體在 Vow.Combat，UI 不引用它）；
        // 護盾只讀 Vow.Core 的 IRockShield。
        private Action _spawnEnemyWall;
        private Action _toggleTurret;
        private Func<bool> _turretFiring;
        private IRockShield _shield;
        private int _shieldShown = -1;
        private string _shieldLabel = "0";

        private float _fpsTimer;
        private int _fpsFrames;
        private int _fps;
        private string _stateName = StateNames[0];
        private string _rigLabel = "?";
        private string _screenLabel = "";
        private Color _rigColor = Color.white;
        private int _refreshHz;

        public void Initialize(HeroController hero, PlayerInputService input, HitboxVisualizer hitboxes,
            NetworkLatencySimulator latency = null, Func<bool> gridVisible = null, Action toggleGrid = null,
            Action spawnEnemyWall = null, Action toggleTurret = null, Func<bool> turretFiring = null,
            IRockShield shield = null,
            Action castWater = null, Action castFire = null, Action castWind = null,
            Action toggleElementFaction = null, Func<bool> elementFactionIsBlue = null,
            ElementCastCooldowns elementCooldowns = null)
        {
            _castWater = castWater;
            _castFire = castFire;
            _castWind = castWind;
            _toggleElementFaction = toggleElementFaction;
            _elementFactionIsBlue = elementFactionIsBlue;
            _elementCooldowns = elementCooldowns;
            _hero = hero;
            _input = input;
            _hitboxes = hitboxes;
            _latency = latency;
            _gridVisible = gridVisible;
            _toggleGrid = toggleGrid;
            _spawnEnemyWall = spawnEnemyWall;
            _toggleTurret = toggleTurret;
            _turretFiring = turretFiring;
            _shield = shield;

            if (_hero != null)
            {
                _hero.StateMachine.OnStateChanged += HandleStateChanged;

                // 常駐標示載體種類：盲測時沒有人會記得自己測的是佔位骨架還是正式的 Humanoid（紅線 3）
                Animator animator = _hero.GetComponentInChildren<Animator>();
                bool humanoid = animator != null && animator.isHuman;
                _rigLabel = humanoid ? "HUMANOID" : "PLACEHOLDER";
                _rigColor = humanoid ? Color.white : new Color(1f, 0.45f, 0.3f);
            }
            if (_input != null)
            {
                _input.OnUiRegionTapped += HandleRegionTapped;
                _modeRegion = _input.Routing.RegisterUiRegion(default);
                _hitboxRegion = _input.Routing.RegisterUiRegion(default);
                if (_latency != null) _latencyRegion = _input.Routing.RegisterUiRegion(default);
                if (_gridVisible != null) _gridRegion = _input.Routing.RegisterUiRegion(default);
                if (_spawnEnemyWall != null) _enemyWallRegion = _input.Routing.RegisterUiRegion(default);
                if (_toggleTurret != null) _turretRegion = _input.Routing.RegisterUiRegion(default);
                if (_castWater != null) _waterRegion = _input.Routing.RegisterUiRegion(default);
                if (_castFire != null) _fireRegion = _input.Routing.RegisterUiRegion(default);
                if (_castWind != null) _windRegion = _input.Routing.RegisterUiRegion(default);
                if (_toggleElementFaction != null) _elemRegion = _input.Routing.RegisterUiRegion(default);
            }
            RecalculateLayout();
        }

        // ── IDebugHudPanel：批 3 驗收面（V4-q）。真實觸控走 HandleRegionTapped，它呼叫的是同樣這兩個方法。──

        public string TurretButtonLabel => _turretFiring != null && _turretFiring() ? "TURRET: ON" : "TURRET: OFF";
        public string EnemyWallButtonLabel => "ENEMY WALL";
        public string ShieldValueLabel => _shieldLabel;

        public void PressTurretButton()
        {
            if (_toggleTurret != null) _toggleTurret();
        }

        public void PressEnemyWallButton()
        {
            if (_spawnEnemyWall != null) _spawnEnemyWall();
        }

        public bool TryGetTurretButtonScreenPoint(out float x, out float y)
        {
            return TryGetButtonScreenPoint(_toggleTurret != null, _turretRect, out x, out y);
        }

        public bool TryGetEnemyWallButtonScreenPoint(out float x, out float y)
        {
            return TryGetButtonScreenPoint(_spawnEnemyWall != null, _enemyWallRect, out x, out y);
        }

        // 用的是餵給 InputRoutingManager 的同一個換算（ToScreenRegion），所以測試點下去的位置
        // 就是實際登記的那個矩形的中心；矩形算錯時會路由到別的區域。
        private bool TryGetButtonScreenPoint(bool present, Rect guiRect, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (!present) return false;

            ScreenRegion region = ToScreenRegion(guiRect);
            x = (region.XMin + region.XMax) * 0.5f;
            y = (region.YMin + region.YMax) * 0.5f;
            return true;
        }

        private void Awake()
        {
            useGUILayout = false; // 關掉 Layout 階段：少跑一輪 OnGUI，也是 IMGUI 零配置的前提
        }

        private void OnDestroy()
        {
            if (_hero != null) _hero.StateMachine.OnStateChanged -= HandleStateChanged;
            if (_input != null) _input.OnUiRegionTapped -= HandleRegionTapped;
        }

        private void Update()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= FpsSampleSeconds)
            {
                _fps = Mathf.RoundToInt(_fpsFrames / _fpsTimer);
                // 面板回報的刷新率（不是實際送幀率，也不是觸控採樣率；Editor 下讀到的是桌面顯示器）。用途只有一個：
                // 讓測試者一眼看出這台裝置的面板上限是不是 120——面板只有 60 時，FPS 顯示再高也沒有意義。
                _refreshHz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }

            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) RecalculateLayout();

            RefreshElementLabels();
            RefreshCaptureLabels();

            // 護盾數值：只在整數變動時查表，不是每幀組字串（IntStringCache 已預先快取，零配置）。
            int shieldNow = _shield != null ? Mathf.RoundToInt(_shield.Amount) : 0;
            if (shieldNow == _shieldShown) return;
            _shieldShown = shieldNow;
            _shieldLabel = IntStringCache.Get(shieldNow);
        }

        // 四顆元素鈕的標籤：只在索引真的變了才換掉快取的字串，而且一律查預建表——**不得字串串接**
        // （OnGUI 每幀都會拿它去畫，串接就是每幀配置）。V5-e 的活性計數在這裡累加。
        private void RefreshElementLabels()
        {
            if (_elementCooldowns != null)
            {
                float now = Time.time;
                int water = _elementCooldowns.RemainingLabelIndex(ElementCast.Water, now);
                if (water != _waterIndexShown)
                {
                    _waterIndexShown = water;
                    _waterLabel = WaterLabels[water < WaterLabels.Length ? water : WaterLabels.Length - 1];
                    ElementLabelRecomputeCount++;
                }

                int fire = _elementCooldowns.RemainingLabelIndex(ElementCast.Fire, now);
                if (fire != _fireIndexShown)
                {
                    _fireIndexShown = fire;
                    _fireLabel = FireLabels[fire < FireLabels.Length ? fire : FireLabels.Length - 1];
                    ElementLabelRecomputeCount++;
                }

                int wind = _elementCooldowns.RemainingLabelIndex(ElementCast.Wind, now);
                if (wind != _windIndexShown)
                {
                    _windIndexShown = wind;
                    _windLabel = WindLabels[wind < WindLabels.Length ? wind : WindLabels.Length - 1];
                    ElementLabelRecomputeCount++;
                }
            }

            if (_elementFactionIsBlue == null) return;
            int blue = _elementFactionIsBlue() ? 1 : 0;
            if (blue == _elemBlueShown) return;
            _elemBlueShown = blue;
            _elemLabel = blue == 1 ? ElemBlueLabel : ElemRedLabel;
            ElementLabelRecomputeCount++;
        }

        private void HandleStateChanged(PlayerState oldState, PlayerState newState)
        {
            _stateName = StateNames[(int)newState]; // Enum.ToString() 會配置，改查預先建好的表
        }

        private void HandleRegionTapped(int regionId)
        {
            if (regionId == _modeRegion && _input != null)
            {
                _input.ActiveMode = _input.ActiveMode == ControlMode.ModeA_FullScreenFlick
                    ? ControlMode.ModeB_DualZonePip
                    : ControlMode.ModeA_FullScreenFlick;
            }
            else if (regionId == _hitboxRegion && _hitboxes != null)
            {
                _hitboxes.Visible = !_hitboxes.Visible;
            }
            else if (regionId == _latencyRegion && _latency != null)
            {
                _latency.CyclePreset();
            }
            else if (regionId == _gridRegion && _toggleGrid != null)
            {
                _toggleGrid();
            }
            else if (regionId == _enemyWallRegion)
            {
                PressEnemyWallButton();
            }
            else if (regionId == _turretRegion)
            {
                PressTurretButton();
            }
            else if (regionId == _waterRegion)
            {
                PressWaterButton();
            }
            else if (regionId == _fireRegion)
            {
                PressFireButton();
            }
            else if (regionId == _windRegion)
            {
                PressWindButton();
            }
            else if (regionId == _elemRegion)
            {
                PressElementFactionButton();
            }
            else if (regionId == _captureRegion)
            {
                PressCaptureButton();
            }
        }

        private void RecalculateLayout()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            float dpi = Screen.dpi;

            // 所有手勢門檻與符印按鈕都以毫米定義、靠 Screen.dpi 換算；dpi 回 0（WebGL 常見）時退回 160，實體尺寸就會失真。
            // 把量到的值秀出來，試玩回報「按鈕太小／太難按」時才分得出是設計值還是換算的問題。只在版面重算時組字串，不是每幀。
            _screenLabel = Mathf.RoundToInt(dpi) + " dpi  " + Screen.width + "x" + Screen.height;

            // 批 4 V4-o：版面計算搬到 Core/Logic 的可注入入口（batchmode 改不了 Screen.*），
            // 既有六個 rect 的算式逐字搬移，這裡只負責把結果抄回 UnityEngine.Rect。
            DebugHudLayout layout = DebugHudLayout.Compute(Screen.width, Screen.height, dpi,
                _latency != null, _gridVisible != null, _toggleTurret != null || _spawnEnemyWall != null);
            _scale = layout.Scale;
            _modeRect = ToRect(layout.Mode);
            _hitboxRect = ToRect(layout.Hitbox);
            _latencyRect = ToRect(layout.Latency);
            _gridRect = ToRect(layout.Grid);
            _enemyWallRect = ToRect(layout.EnemyWall);
            _turretRect = ToRect(layout.Turret);
            _waterRect = ToRect(layout.Water);
            _fireRect = ToRect(layout.Fire);
            _windRect = ToRect(layout.Wind);
            _elemRect = ToRect(layout.Elem);
            _panelHeight = layout.PanelHeight;
            // v0.8.0：右上面板與 CAPTURE 鈕同樣向 DebugHudLayout 要（§2.4／R14）。Off 時面板高 72，佔領模式另取 116 那一份。
            _matchPanelRect = ToRect(layout.MatchPanel);
            _captureRect = ToRect(layout.Capture);
            _matchPanelCaptureHeight = DebugHudLayout.Compute(Screen.width, Screen.height, dpi,
                _latency != null, _gridVisible != null, _toggleTurret != null || _spawnEnemyWall != null, true).MatchPanel.Height;

            if (_input == null) return;
            _input.Routing.UpdateUiRegion(_modeRegion, ToScreenRegion(_modeRect));
            _input.Routing.UpdateUiRegion(_hitboxRegion, ToScreenRegion(_hitboxRect));
            _input.Routing.UpdateUiRegion(_latencyRegion, ToScreenRegion(_latencyRect));
            _input.Routing.UpdateUiRegion(_gridRegion, ToScreenRegion(_gridRect));
            _input.Routing.UpdateUiRegion(_enemyWallRegion, ToScreenRegion(_enemyWallRect));
            _input.Routing.UpdateUiRegion(_turretRegion, ToScreenRegion(_turretRect));
            _input.Routing.UpdateUiRegion(_waterRegion, ToScreenRegion(_waterRect));
            _input.Routing.UpdateUiRegion(_fireRegion, ToScreenRegion(_fireRect));
            _input.Routing.UpdateUiRegion(_windRegion, ToScreenRegion(_windRect));
            _input.Routing.UpdateUiRegion(_elemRegion, ToScreenRegion(_elemRect));
            _input.Routing.UpdateUiRegion(_captureRegion, ToScreenRegion(_captureRect));
        }

        private static Rect ToRect(HudRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        // IMGUI 座標（原點左上、已縮放）→ 螢幕座標（原點左下、像素）
        private ScreenRegion ToScreenRegion(Rect guiRect)
        {
            float xMin = guiRect.xMin * _scale;
            float xMax = guiRect.xMax * _scale;
            float yMax = Screen.height - guiRect.yMin * _scale;
            float yMin = Screen.height - guiRect.yMax * _scale;
            return new ScreenRegion(xMin, yMin, xMax, yMax);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

            // 批 4：面板高度與版面同源（DebugHudLayout.PanelHeight，已含新加的兩列）。
            Fill(new Rect(Pad, Pad, PanelWidth, _panelHeight), PanelColor);

            float x = Pad * 2f;
            float valueX = x + 84f;
            float y = Pad * 1.5f;

            GUI.Label(new Rect(x, y, PanelWidth - Pad * 2f, Row), VowVersion.Label, _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "FPS", _label);
            GUI.Label(new Rect(valueX, y, 160f, Row), IntStringCache.Get(_fps), _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "PANEL HZ", _label);
            GUI.Label(new Rect(valueX, y, 160f, Row), IntStringCache.Get(_refreshHz), _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "STATE", _label);
            GUI.Label(new Rect(valueX, y, 170f, Row), StateLabel, _label);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "PIPS", _label);
            DrawPips(valueX, y + 4f);
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "WINDOW", _label);
            DrawWindowBar(valueX, y + 6f);
            y += Row;

            bool modeB = _input != null && _input.ActiveMode == ControlMode.ModeB_DualZonePip;
            GUI.Label(new Rect(x, y, 84f, Row), "MODE", _label);
            GUI.Label(new Rect(valueX, y, 170f, Row), modeB ? "B  Pip + Tap" : "A  Full-Screen Flick", _label);

            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "RIG", _label);
            Color previousContent = GUI.contentColor;
            GUI.contentColor = _rigColor;
            GUI.Label(new Rect(valueX, y, 170f, Row), _rigLabel, _label);
            GUI.contentColor = previousContent;
            y += Row;

            GUI.Label(new Rect(x, y, 84f, Row), "SCREEN", _label);
            GUI.Label(new Rect(valueX, y, 170f, Row), _screenLabel, _label);
            y += Row;

            // 批 3：破牆護盾（取得後顯示 150，隨 2.5s 倒數遞減至 0）
            GUI.Label(new Rect(x, y, 84f, Row), "SHIELD", _label);
            GUI.Label(new Rect(valueX, y, 170f, Row), _shieldLabel, _label);

            Fill(_modeRect, ButtonColor);
            GUI.Label(_modeRect, modeB ? "Switch to A" : "Switch to B", _buttonLabel);

            bool hitboxOn = _hitboxes != null && _hitboxes.Visible;
            Fill(_hitboxRect, hitboxOn ? ButtonOnColor : ButtonColor);
            GUI.Label(_hitboxRect, hitboxOn ? "Hitbox ON" : "Hitbox OFF", _buttonLabel);

            if (_latency != null)
            {
                LatencyPreset preset = _latency.Preset;
                Fill(_latencyRect, preset == LatencyPreset.Off ? ButtonColor : ButtonOnColor);
                GUI.Label(_latencyRect, preset == LatencyPreset.Off ? "NET delay: OFF"
                                      : preset == LatencyPreset.Ms50 ? "NET delay: 50 ms" : "NET delay: 80 ms", _buttonLabel);
            }

            if (_gridVisible != null)
            {
                bool gridOn = _gridVisible();
                Fill(_gridRect, gridOn ? ButtonOnColor : ButtonColor);
                GUI.Label(_gridRect, gridOn ? "GRID: ON" : "GRID: OFF", _buttonLabel);
            }

            if (_spawnEnemyWall != null)
            {
                Fill(_enemyWallRect, ButtonColor);
                GUI.Label(_enemyWallRect, EnemyWallButtonLabel, _buttonLabel);
            }

            if (_toggleTurret != null)
            {
                bool turretOn = _turretFiring != null && _turretFiring();
                Fill(_turretRect, turretOn ? ButtonOnColor : ButtonColor);
                GUI.Label(_turretRect, TurretButtonLabel, _buttonLabel);
            }

            // ── 批 4：四顆元素除錯鈕。標籤一律查預建字串表（見 Update 的 RefreshElementLabels）──
            if (_castWater != null)
            {
                Fill(_waterRect, ElementsLocked
                    ? ButtonCooldownColor : _waterIndexShown > 0 ? ButtonCooldownColor : ButtonColor);
                GUI.Label(_waterRect, _waterLabel, _buttonLabel);
            }
            if (_castFire != null)
            {
                Fill(_fireRect, ElementsLocked
                    ? ButtonCooldownColor : _fireIndexShown > 0 ? ButtonCooldownColor : ButtonColor);
                GUI.Label(_fireRect, _fireLabel, _buttonLabel);
            }
            if (_castWind != null)
            {
                Fill(_windRect, ElementsLocked
                    ? ButtonCooldownColor : _windIndexShown > 0 ? ButtonCooldownColor : ButtonColor);
                GUI.Label(_windRect, _windLabel, _buttonLabel);
            }
            if (_toggleElementFaction != null)
            {
                Fill(_elemRect, ElementsLocked
                    ? ButtonCooldownColor : _elemBlueShown == 1 ? ElemBlueColor : ElemRedColor);
                GUI.Label(_elemRect, _elemLabel, _buttonLabel);
            }

            DrawDuelPanel();

            GUI.matrix = previous;

            if (modeB) DrawPipWidget();
        }

        private void DrawDuelPanel()
        {
            if (_duelRound == null || _hero == null || _opponentHealth == null) return;
            // v0.8.0：位置改由 DebugHudLayout.MatchPanel 算（與 v0.7.0 的原式逐字相同，§2.4）；Off 時高 72，佔領模式 116。
            bool captureMode = InCaptureMode;
            float x = _matchPanelRect.x;
            float y = _matchPanelRect.y;
            Fill(new Rect(x, y, _matchPanelRect.width, captureMode ? _matchPanelCaptureHeight : _matchPanelRect.height), PanelColor);
            GUI.Label(new Rect(x + 8f, y + 4f, 162f, Row), MatchStatusLabel, _label);
            GUI.Label(new Rect(x + 8f, y + 26f, 92f, Row), "HERO HP", _label);
            GUI.Label(new Rect(x + 105f, y + 26f, 56f, Row), IntStringCache.Get(Mathf.CeilToInt(_hero.Health)), _label);
            GUI.Label(new Rect(x + 8f, y + 48f, 92f, Row), "RED HP", _label);
            GUI.Label(new Rect(x + 105f, y + 48f, 56f, Row), IntStringCache.Get(Mathf.CeilToInt(_opponentHealth())), _label);

            if (captureMode)
            {
                // 第 3 列比分（字串查 CaptureHudLabels 的 0～1012 表，不用上限 999 的 IntStringCache，R6）；
                // 第 4 列復活倒數只在英雄倒地時顯示。
                GUI.Label(new Rect(x + 8f, y + 70f, 44f, Row), "BLUE", _label);
                GUI.Label(new Rect(x + 50f, y + 70f, 40f, Row), _blueScoreLabel, _label);
                GUI.Label(new Rect(x + 92f, y + 70f, 36f, Row), "RED", _label);
                GUI.Label(new Rect(x + 126f, y + 70f, 44f, Row), _redScoreLabel, _label);
                string respawn = CaptureRespawnLabel;
                if (respawn != null) GUI.Label(new Rect(x + 8f, y + 92f, 162f, Row), respawn, _label);
            }

            if (_pressCapture == null) return;
            // CAPTURE 鈕：不論模式都畫在同一位置；按了無效的狀態反灰但照樣攔截觸控（E27）。
            bool captureInvalid = CurrentGate().CaptureButton == CaptureButtonAction.Invalid;
            Fill(_captureRect, captureInvalid ? ButtonCooldownColor : captureMode ? ButtonOnColor : ButtonColor);
            GUI.Label(_captureRect, CaptureButtonLabel, _buttonLabel);
        }

        private void DrawPips(float x, float y)
        {
            if (_hero == null) return;
            ICadenceMover mover = _hero.CadenceMover;
            int max = mover.MaxCharges;
            int current = mover.CurrentCharges;

            for (int i = 0; i < max; i++)
            {
                Rect pip = new Rect(x + i * 26f, y, 20f, 14f);
                Fill(pip, i < current ? PipOn : PipOff);

                // 正在回充的那一格：由左往右填滿
                if (i == current)
                {
                    pip.width *= mover.ChargeRecoveryNormalized;
                    Fill(pip, new Color(PipOn.r, PipOn.g, PipOn.b, 0.45f));
                }
            }
        }

        // 220ms 目押窗口倒數：命中瞬間滿格，隨時間縮短；條還在＝現在微彈就會切後搖。
        private void DrawWindowBar(float x, float y)
        {
            const float width = 150f;
            Fill(new Rect(x, y, width, 10f), PipOff);
            if (_hero == null) return;

            float remaining = _hero.CadenceWindowRemainingNormalized;
            if (remaining > 0f) Fill(new Rect(x, y, width * remaining, 10f), WindowColor);
        }

        // 模式 B 微輪盤：畫出左下判定區，以及按住時的浮動原點與 3.5mm／7.5mm 兩圈（以螢幕像素繪製，不套 HUD 縮放）。
        private void DrawPipWidget()
        {
            float zone = _input.PipZonePixels;
            Fill(new Rect(0f, Screen.height - zone, zone, zone), new Color(1f, 1f, 1f, 0.06f));

            if (!_input.IsPipHeld) return;

            Vector2 origin = _input.PipOrigin;
            float guiY = Screen.height - origin.y;
            float min = _input.FlickMinRadiusPixels;
            float max = _input.FlickMaxRadiusPixels;

            Fill(new Rect(origin.x - max, guiY - max, max * 2f, max * 2f), new Color(1f, 1f, 1f, 0.10f));
            Fill(new Rect(origin.x - min, guiY - min, min * 2f, min * 2f), new Color(0f, 0f, 0f, 0.35f));

            if (!_input.HasPipVector) return;
            Vector2 dir = _input.PipDirection;
            float knob = min * 0.8f;
            float kx = origin.x + dir.x * max;
            float ky = guiY - dir.y * max;
            Fill(new Rect(kx - knob * 0.5f, ky - knob * 0.5f, knob, knob), PipOn);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = Color.white;

            _buttonLabel = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
    }
}
