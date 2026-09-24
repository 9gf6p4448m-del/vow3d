using System;
using UnityEngine;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Vow.UI;

namespace Vow.Bootstrap
{
    // Phase 1 灰盒場景的組裝根 (Composition Root)：全場唯一知道「誰是具體類別」的地方。
    // 各模組只認得 Vow.Core 裡的介面；把它們接起來的工作集中在這裡，換實作、接測試替身都只改這一個檔。
    [DefaultExecutionOrder(-1000)]
    public sealed class Phase1Bootstrap : MonoBehaviour
    {
        private const int TargetFrameRate = 120;

        [SerializeField] private HeroController _hero;
        [SerializeField] private PlayerInputService _input;
        [SerializeField] private NetworkLatencySimulator _latency;
        [SerializeField] private HapticFeedbackService _haptics;
        [SerializeField] private CombatFeedbackService _feedback;
        [SerializeField] private SkillTelegraphService _telegraph;
        [SerializeField] private FollowCameraRig _cameraRig;
        [SerializeField] private Camera _camera;
        [SerializeField] private DebugHud _hud;
        [SerializeField] private HitboxVisualizer _hitboxes;
        [SerializeField] private CadenceAimPreview _aimPreview;
        [SerializeField] private HeroTuningAsset _tuningAsset;
        [SerializeField] private RuneCaster _runeCaster;
        [SerializeField] private RuneGhostPreview _runeGhost;
        [SerializeField] private RuneButtonView _runeButton;
        [SerializeField] private NavGridDebugView _navGridDebug;
        [SerializeField] private Transform _arenaBoundary;
        [SerializeField] private TrainingOpponent _opponent;
        [SerializeField] private SkillTelegraphService _opponentTelegraph;

        private readonly DuelTuning _duelTuning = new DuelTuning();
        private DuelRoundLogic _duelRound;
        private DuelInputRouter _duelInput;
        private Vector3 _heroSpawn;
        private HeroLocomotion _opponentLocomotion;

        // ── Phase 2 批 3：陣營校驗破牆得護盾＋友軍彈道穿透 ──
        // 玩家石牆池與除錯用的敵方石牆池由**兩個父物件**分開（§4-6）：敵方牆也是 RuneWall，
        // 繼續用 FindObjectsOfType 整批當池的話會佔掉玩家的 2 面上限。
        [SerializeField] private Transform _runeWallPool;
        [SerializeField] private Transform _enemyWallPool;
        [SerializeField] private TestTurret _turret;
        [SerializeField] private EnemyWallSpawner _enemyWalls;
        [SerializeField] private RockShieldBehaviour _shield;
        [SerializeField] private HeroShieldBar _shieldBar;

        // ── Phase 2 批 4：三大元素反應 ──
        [SerializeField] private ElementField _elementField;
        [SerializeField] private SectorTelegraph _sectorTelegraph;
        [SerializeField] private Transform _elementZonePool;

        // ── v0.6.1：反應飄字／受困狀態回饋（V061_FEEDBACK_PLAN.md §1）──
        private ReactionCalloutDisplay _callouts;

        private readonly ColliderTargetRegistry _targets = new ColliderTargetRegistry();
        private readonly ProjectileTuning _projectileTuning = new ProjectileTuning();

        private readonly ElementTuning _elementTuning = new ElementTuning();
        private CombatTargetRoster _elementRoster;
        private ElementCastCooldowns _elementCooldowns;
        private Faction _elementFaction = Faction.BlueTeam;   // ELEM 鈕：下一發技能的陣營

        // ── Phase 2 批 2：0.5m 阻擋格點。全場唯一一份，牆登記進來、英雄從這裡拿繞牆方向 ──
        private readonly NavGridTuning _navTuning = new NavGridTuning();
        private BlockGrid _navGrid;
        private GridNavigator _navigator;
        private HeroLocomotion _heroLocomotion;
        private System.Action<CombatTargetBehaviour> _navStampedHandler; // 只建一次，執行期零配置

        public BlockGrid NavGrid => _navGrid;
        public GridNavigator Navigator => _navigator;
        public TrainingOpponent Opponent => _opponent;
        public DuelRoundState DuelState => _duelRound != null ? _duelRound.State : DuelRoundState.Dormant;
        public DuelTuning DuelTuning => _duelTuning;
        public int DuelStartCount => _duelRound != null ? _duelRound.StartCount : 0;

        public void SetDuelLatencyPreset(int milliseconds)
        {
            if (_latency == null) return;
            _latency.Preset = milliseconds == 80 ? LatencyPreset.Ms80
                            : milliseconds == 50 ? LatencyPreset.Ms50 : LatencyPreset.Off;
        }

        // 批 3：PlayMode 測試 asmdef 只看得到 Vow.Core／Vow.Combat／Vow.Bootstrap（V6 不得加引用），
        // 所以輸入服務與除錯 HUD 一律以 Vow.Core 的介面型別從這裡交出去。
        public IPlayerInputService InputService => _input;
        public IWorldTapInput WorldTapInput => _input;
        public IDebugHudPanel HudPanel => _hud;
        public TestTurret Turret => _turret;
        public EnemyWallSpawner EnemyWalls => _enemyWalls;
        public IRockShield Shield => _shield;
        // 驗收替一面牆加上第二個 Collider 之後要重新登記查表（r1 對抗審查 HIGH-3 的量法）。
        public ColliderTargetRegistry TargetRegistry => _targets;

        // ── 批 4 的驗收面：四顆鈕走的是與真實觸控同一個入口（DebugHud 的 PressXxxButton）──
        public ElementField ElementField => _elementField;
        public ElementTuning ElementTuning => _elementTuning;
        public CombatTargetRoster ElementRoster => _elementRoster;
        public SectorTelegraph SectorTelegraph => _sectorTelegraph;
        public Faction ElementCastFaction => _elementFaction;
        public int ElementZoneViewPoolSize => _elementField != null ? _elementField.ViewPoolSize : 0;

        public void PressElementWaterButton() { if (_hud != null) _hud.PressWaterButton(); }
        public void PressElementFireButton() { if (_hud != null) _hud.PressFireButton(); }
        public void PressElementWindButton() { if (_hud != null) _hud.PressWindButton(); }
        public void PressElementFactionButton() { if (_hud != null) _hud.PressElementFactionButton(); }

        public string ElementWaterButtonLabel => _hud != null ? _hud.WaterButtonLabel : null;
        public string ElementFireButtonLabel => _hud != null ? _hud.FireButtonLabel : null;
        public string ElementWindButtonLabel => _hud != null ? _hud.WindButtonLabel : null;
        public string ElementFactionButtonLabel => _hud != null ? _hud.ElementFactionButtonLabel : null;
        public int ElementLabelRecomputeCount => _hud != null ? _hud.ElementLabelRecomputeCount : 0;

        // ── v0.6.1 的驗收面：PlayMode 測試 asmdef 看不到 Vow.UI，STATE 列的顯示值改由這裡轉交 ──
        public ReactionCalloutDisplay Callouts => _callouts;
        public string HudStateLabel => _hud != null ? _hud.StateLabel : null;

        // ───────────────────── v0.8.0：七塊板塊佔領迴圈（V080_CAPTURE_PLAN.md §2.1-3）─────────────────────
        // 規則與時序全部在 CaptureMatchLogic（純邏輯、已驗收）；這裡只做 Unity 端的接線：每幀 Tick、受傷／倒地分派、
        // 開局清場、復活傳送、牆推出、結算凍結與回待機。不按 CAPTURE 時（Off）這一段不改變單挑的任何一條路徑。
        [SerializeField] private CaptureBoardView _captureBoard;
        private readonly CaptureTuning _captureTuning = new CaptureTuning();
        private CaptureMatchLogic _capture;
        private CaptureMatchView _captureView;
        private CombatTargetBehaviour[] _navBlockers = new CombatTargetBehaviour[0]; // 復活傳送後逐一推出（R10）
        private Action<float> _opponentDamagedHandler;                             // 只建一次，執行期零配置

        public CaptureMatchState CaptureState => _capture != null ? _capture.State : CaptureMatchState.Off;
        public ICaptureMatchView CaptureView => _captureView;
        public int CaptureStartCount => _capture != null ? _capture.StartCount : 0;
        public CaptureBoardView CaptureBoard => _captureBoard;
        public CaptureTuning CaptureTuning => _captureTuning;
        public int CaptureFlipCount => _capture != null ? _capture.FlipCount : 0;
        public int CaptureScoreTickCount => _capture != null ? _capture.ScoreTickCount : 0;
        public int CaptureContestTickCount => _capture != null ? _capture.ContestTickCount : 0;
        public int CaptureInterruptCount => _capture != null ? _capture.InterruptCount : 0;
        public int CaptureRespawnCount => _capture != null ? _capture.RespawnCount : 0;

        // CAPTURE 鈕走與真實觸控同一個入口（DebugHud.PressCaptureButton）；螢幕點＝登記給 InputRoutingManager 的矩形中心。
        public void PressCaptureButton() { if (_hud != null) _hud.PressCaptureButton(); }

        public bool TryGetCaptureButtonScreenPoint(out float x, out float y)
        {
            x = 0f;
            y = 0f;
            return _hud != null && _hud.TryGetCaptureButtonScreenPoint(out x, out y);
        }

        // HUD 字串代理（PlayMode 測試 asmdef 看不到 Vow.UI，比照 HudStateLabel）。
        public string CaptureButtonLabel => _hud != null ? _hud.CaptureButtonLabel : null;
        public string MatchStatusLabel => _hud != null ? _hud.MatchStatusLabel : null;
        public string CaptureBlueScoreLabel => _hud != null ? _hud.CaptureBlueScoreLabel : null;
        public string CaptureRedScoreLabel => _hud != null ? _hud.CaptureRedScoreLabel : null;
        public string CaptureRespawnLabel => _hud != null ? _hud.CaptureRespawnLabel : null;
        public int CaptureScoreLabelRecomputeCount => _hud != null ? _hud.CaptureScoreLabelRecomputeCount : 0;
        public int CaptureRespawnLabelRecomputeCount => _hud != null ? _hud.CaptureRespawnLabelRecomputeCount : 0;

#if UNITY_EDITOR
        // PlayMode 終局用的比分種子入口（R12）：只寫兩個比分整數，不動計分時鐘、歸屬、進度。正式建置不編進去（V-D01）。
        public void SeedCaptureScoresForTest(int blueScore, int redScore)
        {
            if (_capture != null) _capture.SeedScoresForTest(blueScore, redScore);
        }
#endif

        // 符印鈕中心（螢幕座標）：與 RuneButtonView 同一個 RuneButtonLayout 算式——點下去就是真的符印觸控。
        public bool TryGetRuneButtonScreenPoint(out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (_input == null) return false;
            ScreenRegion button = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter).Button;
            x = (button.XMin + button.XMax) * 0.5f;
            y = (button.YMin + button.YMax) * 0.5f;
            return true;
        }

        // 按住不放的模擬手指（V-B14：倒地前開始、復活後才放手的符印手勢）；走與真實手指相同的分流路徑。
        public void BeginScreenHold(float x, float y) { if (_input != null) _input.BeginSimulatedHold(x, y); }
        public void MoveScreenHold(float x, float y) { if (_input != null) _input.MoveSimulatedHold(x, y); }
        public void EndScreenHold() { if (_input != null) _input.EndSimulatedHold(); }

        private void Awake()
        {
            // 紅線 6：畫面與輸入鎖定 120Hz（輸入取樣頻率由 PlayerInputService 設定 InputSystem.pollingFrequency）。
            QualitySettings.vSyncCount = 0;

            int frameRate = TargetFrameRate;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 瀏覽器例外：WebGL 一旦指定幀率，Unity 會改用 setTimeout 排程而不是 requestAnimationFrame，畫面反而卡頓。
            // -1 = 交給瀏覽器跟著螢幕刷新率跑（120Hz 螢幕就是 120）。網頁版僅供試玩，120Hz 的正式驗收以原生建置為準。
            frameRate = -1;
#endif
            Application.targetFrameRate = frameRate;

            ResolveMissingReferences();
        }

        // 放在 Start：此時所有物件的 Awake 都已跑完（HeroController 的狀態機、各目標的 Collider 快取都已就緒）。
        private void Start()
        {
            if (_hero == null || _input == null)
            {
                Debug.LogError("[VOW] 場景缺少 HeroController 或 PlayerInputService，請執行 VOW/Phase 1/Build Greybox Scene。", this);
                return;
            }

            CombatTargetBehaviour[] targets = FindObjectsOfType<CombatTargetBehaviour>();
            // 批 4 §4-5：元素 AOE 需要一份**可列舉且去重**的名冊（ColliderTargetRegistry 的鍵是 collider，
            // 一個目標多個 Collider 會重複結算）。兩者在這裡**成對**登記，分母歸一。
            _elementRoster = new CombatTargetRoster(_elementTuning.TargetRosterCapacity);
            for (int i = 0; i < targets.Length; i++)
            {
                _targets.Register(targets[i]);
                RegisterElementTarget(targets[i]);
            }

            BuildNavGrid(targets);
            _heroSpawn = _hero.transform.position;
            if (_opponent != null)
            {
                _duelRound = new DuelRoundLogic(_duelTuning);
                _opponentLocomotion = _opponent.GetComponent<HeroLocomotion>();
                _opponent.Initialize(_hero, _opponentTelegraph, _duelTuning,
                                     new GridNavigator(_navGrid, _navTuning), _navTuning.BodyRadius);

                _capture = new CaptureMatchLogic(_captureTuning);
                _captureView = new CaptureMatchView(_capture);
                _opponent.ConfigureCapture(_captureView, _captureTuning);
                InitializeCaptureBoard();
            }
            _navBlockers = CollectNavBlockers(targets);

            _input.Initialize(_targets, _camera);
            // 批 3：「哪些石牆算自家牆」由本地陣營決定（點自家牆＝點到牆後的地板，§4-1）。
            _input.SetLocalFaction(_hero.HeroFaction);

            // 英雄訂閱的是延遲注入層（預設 OFF＝同一呼叫內直通）；HUD 與預警箭頭讀的仍是真正的輸入服務——
            // 它們屬於本地表現，不該跟著模擬的網路延遲一起變慢。
            IPlayerInputService heroInput = _input;
            IRuneCastInput heroRuneInput = _input;
            if (_latency != null)
            {
                _latency.Initialize(_input);
                heroInput = _latency;
                heroRuneInput = _latency;
            }
            if (_duelRound != null)
            {
                _duelInput = new DuelInputRouter(heroInput, heroRuneInput, _opponent, _duelRound, _captureView);
                _duelInput.OnStartRequested += StartDuel;
                _duelInput.OnCaptureStartRequested += StartCapture;
                heroInput = _duelInput;
                heroRuneInput = _duelInput;
            }
            _hero.Initialize(heroInput, _feedback, _camera, _haptics);

            // 符印石牆：極速施放／鬆手成牆走延遲注入層（heroInput／heroRuneInput，同英雄本體）；
            // 虛影與按鈕是純本地回饋，直接訂閱 _input，不經延遲（計畫書 §4 假設 11）。
            if (_runeCaster != null && _tuningAsset != null)
            {
                // §4-6：只拿 RuneWallPool 底下那幾面。FindObjectsOfType 會把除錯用的敵方牆一起掃進來，
                // 敵方牆就會佔掉玩家的 2 面上限（V4-o 守這條）。
                RuneWall[] runeWallPool = CollectWalls(_runeWallPool, "RuneWallPool");
                _runeCaster.Initialize(heroInput, heroRuneInput, _hero.transform, _camera, _tuningAsset.Rune, runeWallPool, _hero.HeroFaction);
            }

            InitializeBatch3(targets);
            InitializeBatch4(targets);
            if (_duelRound != null)
            {
                _hero.ConfigureDuel(_duelTuning, _shield);
                // v0.8.0：倒地依模式分派（§2.1-3）——佔領對局中走 CaptureMatchLogic，其餘照舊走 HandleDuelKnockout。
                _hero.OnKnockedOut += HandleHeroKnockedOut;
                _opponent.OnDied += HandleOpponentDied;
                _hero.OnDuelDamaged += HandleHeroDamaged;
                _opponentDamagedHandler = HandleOpponentDamaged;
                _opponent.OnDamaged += _opponentDamagedHandler;
            }
            // 「此刻放手會不會取消」是本機回饋，讀未經延遲的 _input，lambda 只在這裡建一次。
            if (_runeGhost != null && _tuningAsset != null)
                _runeGhost.Initialize(_input, _input, _hero.transform, _camera, _tuningAsset.Rune, () => _input.IsRuneCancelArmed);
            if (_runeButton != null && _tuningAsset != null)
                _runeButton.Initialize(_input, _tuningAsset.Rune, () => _runeCaster != null ? _runeCaster.CooldownRemaining : 0f);

            if (_feedback != null)
            {
                MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                    if (behaviours[i] is IHitstopParticipant participant) _feedback.RegisterHitstopParticipant(participant);

                _hero.StateMachine.OnStateChanged += HandleHeroStateChanged;
            }

            if (_cameraRig != null) _cameraRig.SetTarget(_hero.transform);
            if (_hud != null)
            {
                Func<bool> gridVisible = _navGridDebug != null ? (Func<bool>)IsGridDebugVisible : null;
                Action toggleGrid = _navGridDebug != null ? (Action)ToggleGridDebug : null;
                Action spawnEnemyWall = _enemyWalls != null ? (Action)SpawnEnemyWall : null;
                Action toggleTurret = _turret != null ? (Action)ToggleTurret : null;
                Func<bool> turretFiring = _turret != null ? (Func<bool>)IsTurretFiring : null;
                Action castWater = _elementField != null ? (Action)CastElementWater : null;
                Action castFire = _elementField != null ? (Action)CastElementFire : null;
                Action castWind = _elementField != null ? (Action)CastElementWind : null;
                Action toggleElement = _elementField != null ? (Action)ToggleElementFaction : null;
                Func<bool> elementBlue = _elementField != null ? (Func<bool>)IsElementFactionBlue : null;
                _hud.Initialize(_hero, _input, _hitboxes, _latency, gridVisible, toggleGrid,
                                spawnEnemyWall, toggleTurret, turretFiring, _shield,
                                castWater, castFire, castWind, toggleElement, elementBlue, _elementCooldowns);
                if (_duelRound != null) _hud.ConfigureDuel(ReadOpponentHealth, _duelRound);
                if (_captureView != null) _hud.ConfigureCapture(_captureView, HandleCaptureButton);
            }
            if (_aimPreview != null) _aimPreview.Initialize(_hero, _input, _telegraph, _camera);
        }

        private void OnDestroy()
        {
            if (_hero != null && _feedback != null) _hero.StateMachine.OnStateChanged -= HandleHeroStateChanged;
            if (_hero != null) _hero.OnRootedStarted -= HandleHeroRooted;
            if (_hero != null) _hero.OnKnockedOut -= HandleHeroKnockedOut;
            if (_hero != null) _hero.OnDuelDamaged -= HandleHeroDamaged;
            if (_opponent != null) _opponent.OnDied -= HandleOpponentDied;
            if (_opponent != null && _opponentDamagedHandler != null) _opponent.OnDamaged -= _opponentDamagedHandler;
            if (_duelInput != null)
            {
                _duelInput.OnStartRequested -= StartDuel;
                _duelInput.OnCaptureStartRequested -= StartCapture;
                _duelInput.Dispose();
            }
        }

        private void Update()
        {
            // v0.8.0（R15）：佔領 Tick 必須排在單挑的提早 return 之前，否則永遠跑不到。
            TickCapture(Time.deltaTime);
            if (_duelRound == null || !_duelRound.Tick(Time.deltaTime)) return;
            _latency?.CancelPendingForRound();
            _input?.CancelActiveGesturesForRound();
            _hero.ResetForDuel(_heroSpawn);
            _opponent.ResetForRound();
            _runeCaster?.ResetForRound();
            _elementCooldowns?.ResetForRound();
        }

        private void StartDuel()
        {
            if (_duelRound == null || !_duelRound.TryStart()) return;
            _hero.CancelCombatForDuel();
            _shield?.Clear();
            _elementField?.ClearZonesForDuel();
            _elementCooldowns?.ResetForRound();
            _runeCaster?.ResetForRound();
            _turret?.CancelProjectilesForRound();
            _latency?.CancelPendingForRound();
            _opponent.StartRound();
        }

        private void HandleDuelKnockout()
        {
            if (_duelRound == null || !_duelRound.Knockout()) return;
            _hero.CancelCombatForDuel();
            _opponent.StopRound();
            _opponentTelegraph?.HideIndicator();
            _latency?.CancelPendingForRound();
            _input?.CancelActiveGesturesForRound();
        }

        private float ReadOpponentHealth() { return _opponent != null ? _opponent.Health : 0f; }

        // ───────────────────── v0.8.0：佔領迴圈的 Unity 端接線 ─────────────────────

        private void InitializeCaptureBoard()
        {
            if (_captureBoard == null)
            {
                Debug.LogError("[VOW] 場景缺少 CaptureBoard 引用：佔領模式看不到板塊、晶塔與進度盤（規則照跑）。" +
                               "請執行 VOW/Phase 1/Build Greybox Scene 重建場景。", this);
                return;
            }
            _captureBoard.Initialize(_captureView, _captureTuning);
            _captureBoard.SetShown(false); // Off 時整組不啟用（V-B01）
        }

        // 全場會擋路的牆（符印牆池、敵方牆池、兩面測試牆）。只在 Start 收一次，復活傳送後逐一推出。
        private static CombatTargetBehaviour[] CollectNavBlockers(CombatTargetBehaviour[] targets)
        {
            int count = 0;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] is RuneWall || targets[i] is TestWallTarget) count++;
            CombatTargetBehaviour[] blockers = new CombatTargetBehaviour[count];
            int next = 0;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] is RuneWall || targets[i] is TestWallTarget) blockers[next++] = targets[i];
            return blockers;
        }

        private MatchGateDecision CurrentGate()
        {
            return MatchGate.Evaluate(DuelState, CaptureState, _capture == null || !_capture.BlueKnockedOut);
        }

        // 元素／砲台／敵牆的閘門（R3）：Off 時與 v0.7.0 的「DuelState != Dormant」逐列相同（V-A20）。
        private bool ElementsLocked => CurrentGate().ElementsLocked;

        // Tick 內的順序凍結在 CaptureMatchLogic（§2.1-1）；這裡在它之後依序取出一次性事件：復活 → 結束 → 回待機。
        private void TickCapture(float dt)
        {
            if (_capture == null || _capture.State == CaptureMatchState.Off) return;

            CaptureMatchState before = _capture.State;
            Vector3 heroPosition = _hero.transform.position;
            Vector3 opponentPosition = _opponent.transform.position;
            _capture.Tick(dt, heroPosition.x, heroPosition.z, opponentPosition.x, opponentPosition.z);

            if (_capture.TryConsumeBlueRespawn(out float blueX, out float blueZ)) RespawnHero(blueX, blueZ);
            if (_capture.TryConsumeRedRespawn(out float redX, out float redZ)) RespawnOpponent(redX, redZ);
            if (before == CaptureMatchState.Active && _capture.State == CaptureMatchState.Ended) EnterCaptureEndPause();
            if (_capture.TryConsumeJustReturnedToLobby()) ReturnToCaptureLobby();
        }

        // 佔領開局（E18）：清場規則直接沿用 v0.7.0 的 StartDuel；另外雙方傳送到各自基地復活點並補滿血。
        // 敵方牆一併收掉（E28：待機時可用的測試設施「開局時清掉」）。
        private void StartCapture()
        {
            if (_capture == null || !_capture.TryStart()) return;
            _hero.CancelCombatForDuel();
            _shield?.Clear();
            _elementField?.ClearZonesForDuel();
            _elementCooldowns?.ResetForRound();
            _runeCaster?.ResetForRound();
            _turret?.CancelProjectilesForRound();
            _latency?.CancelPendingForRound();
            CollapseEnemyWalls();

            _hero.ResetForDuel(new Vector3(_captureTuning.BlueHomeRespawnX, _heroSpawn.y, _captureTuning.BlueHomeRespawnZ));
            _hero.SetBodyHidden(false);
            _opponent.RespawnAt(new Vector3(_captureTuning.RedHomeRespawnX, _opponent.transform.position.y,
                                            _captureTuning.RedHomeRespawnZ));
        }

        private void CollapseEnemyWalls()
        {
            if (_enemyWalls == null || _enemyWalls.Pool == null) return;
            RuneWall[] pool = _enemyWalls.Pool;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && pool[i].IsAlive) pool[i].CollapseWall(false);
        }

        private void HandleHeroKnockedOut()
        {
            if (CaptureState == CaptureMatchState.Active)
            {
                // 佔領：5 秒後回基地復活，歸屬與比分保留（裁定 4）。倒地期間身體關掉（E19），
                // 延遲層與進行中的手勢清掉，免得倒地前按下、復活後才放手的指令生效（V-B14）。
                _capture.NotifyKnockedOut(CaptureMatchLogic.BlueFactionId);
                _hero.SetBodyHidden(true);
                _latency?.CancelPendingForRound();
                _input?.CancelActiveGesturesForRound();
                return;
            }
            HandleDuelKnockout();
        }

        private void HandleOpponentDied()
        {
            if (CaptureState == CaptureMatchState.Active)
            {
                _capture.NotifyKnockedOut(CaptureMatchLogic.RedFactionId);
                _opponent.SetBodyHidden(true); // TrainingOpponent.HandleDeath 另外會 StopRound（停 AI、收預警）
                return;
            }
            HandleDuelKnockout();
        }

        // 受傷打斷引導（E11）：英雄的事件在扣護盾之前發（護盾全額吸收也算）；對手走既有的 OnDamaged。
        private void HandleHeroDamaged()
        {
            if (CaptureState == CaptureMatchState.Active) _capture.NotifyDamaged(CaptureMatchLogic.BlueFactionId);
        }

        private void HandleOpponentDamaged(float applied)
        {
            if (CaptureState == CaptureMatchState.Active) _capture.NotifyDamaged(CaptureMatchLogic.RedFactionId);
        }

        // 復活（E20／E21）：座標由 CaptureMatchLogic 在到期那個 tick 決定；補滿血並沿用 ResetForDuel 的清單。
        private void RespawnHero(float x, float z)
        {
            _latency?.CancelPendingForRound();
            _input?.CancelActiveGesturesForRound();
            _hero.ResetForDuel(new Vector3(x, _heroSpawn.y, z));
            _hero.SetBodyHidden(false);
            EjectFromLiveWalls(_heroLocomotion);
        }

        private void RespawnOpponent(float x, float z)
        {
            _opponent.RespawnAt(new Vector3(x, _opponent.transform.position.y, z));
            EjectFromLiveWalls(_opponentLocomotion);
        }

        // R10：HandleNavBlockerStamped 只推「立牆當下」被壓住的人（而且對手只在 IsEngaged 時才推），
        // 倒地期間立在復活點上的牆推不到剛復活的人——所以復活傳送後對仍存活的每一面牆各推一次。
        private void EjectFromLiveWalls(HeroLocomotion body)
        {
            if (body == null) return;
            for (int i = 0; i < _navBlockers.Length; i++)
            {
                CombatTargetBehaviour wall = _navBlockers[i];
                if (wall == null || !wall.IsAlive) continue;
                if (!wall.TryGetNavBlockerBox(out float centerX, out float centerZ, out float normalX, out float normalZ,
                                              out float halfWidth, out float halfThickness))
                    continue;
                body.EjectFromBox(centerX, centerZ, normalX, normalZ, halfWidth, halfThickness);
            }
        }

        // 結算停頓（E17）：凍結英雄輸入與對手 AI；計分、引導、復活由 CaptureMatchLogic 自己停住。
        private void EnterCaptureEndPause()
        {
            _hero.CancelCombatForDuel();
            _opponent.StopRound();
            _opponentTelegraph?.HideIndicator();
            _latency?.CancelPendingForRound();
            _input?.CancelActiveGesturesForRound();
        }

        // 回佔領待機（E17／E22）：雙方回 v0.7.0 出生點並補滿血，收牆走正常 CollapseWall；歸屬、比分、結果保留顯示。
        private void ReturnToCaptureLobby()
        {
            _latency?.CancelPendingForRound();
            _input?.CancelActiveGesturesForRound();
            _hero.ResetForDuel(_heroSpawn);
            _hero.SetBodyHidden(false);
            _opponent.ReturnToCaptureLobby();
            _runeCaster?.ResetForRound();
            _elementCooldowns?.ResetForRound();
        }

        // CAPTURE 鈕（E27）：只在「單挑待機」與「佔領待機」之間切換；其餘狀態按了無效（觸控照樣被攔下）。
        private void HandleCaptureButton()
        {
            if (_capture == null) return;
            CaptureButtonAction action = CurrentGate().CaptureButton;
            if (action == CaptureButtonAction.EnterCaptureMode)
            {
                if (!_capture.TryEnterCaptureMode()) return;
                _opponent.SetCaptureMode(true);
                if (_captureBoard != null) _captureBoard.SetShown(true);
            }
            else if (action == CaptureButtonAction.ExitCaptureMode)
            {
                if (!_capture.TryExitCaptureMode()) return;
                _opponent.SetCaptureMode(false);
                if (_captureBoard != null) _captureBoard.SetShown(false);
            }
        }

        // ───────────────────── Phase 2 批 2：阻擋格點的組裝 ─────────────────────

        // 建立全場唯一的 BlockGrid／GridNavigator，把英雄接上去，並讓每一面石牆知道要登記到哪張格點。
        // 進格點的只有符印石牆與 TestWall_A/B（計畫書 §4 假設 2）；木樁不進格點。
        private void BuildNavGrid(CombatTargetBehaviour[] targets)
        {
            _navGrid = new BlockGrid(_navTuning.OriginX, _navTuning.OriginZ, _navTuning.CellSize,
                                     _navTuning.Columns, _navTuning.Rows);
            _navigator = new GridNavigator(_navGrid, _navTuning);
            RegisterArenaBoundary();

            _heroLocomotion = _hero.GetComponent<HeroLocomotion>();
            if (_heroLocomotion != null) _heroLocomotion.SetNavigator(_navigator, _navTuning.BodyRadius);

            _navStampedHandler = HandleNavBlockerStamped;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is RuneWall runeWall) runeWall.SetNavGrid(_navGrid, _navTuning.BodyRadius, _navStampedHandler);
                else if (targets[i] is TestWallTarget testWall) testWall.SetNavGrid(_navGrid, _navTuning.BodyRadius, _navStampedHandler);
            }

            if (_navGridDebug != null) _navGridDebug.Initialize(_navGrid);
        }

        // r1 對抗審查 H1（§6 R3／R3a）：格點最外一圈是「自由但英雄到不了」的地方——向量場會把英雄導進去、
        // 讓他頂在看不見的邊界上不動。
        // R3a：只擋「格心落在可達範圍之外」的格，不用方塊相交（那會連第 78 圈一起擋掉，
        // 使「沒有牆擋路時」點場地邊緣 19.0～19.45m 被替代到 18.75，違反 Phase 1 手感不變）。
        // 可達範圍＝場景裡實際的邊界 BoxCollider 內面 − BodyRadius（不寫死 19.45）；
        // 現行幾何下這剛好是最外一圈 4×80−4 = 316 格。
        private void RegisterArenaBoundary()
        {
            RegisterArenaBoundary(_arenaBoundary);
        }

        // r2 對抗審查 N2（§6 R11 V11-e）：邊界圈建不出來時不得無聲退回 H1 的狀態（英雄頂死看不見的邊界）。
        // 收 boundary 當參數是為了讓測試能在退化狀態下觸發它——正式路徑一律走上面那個無參數版本。
        public void RegisterArenaBoundary(Transform boundary)
        {
            // r2 對抗審查 N2（§6 R11 V11-e）：這兩條退化路徑原本都是**無聲**的 return——場地邊界圈一格都不登記，
            // 審查 H1 的「英雄被向量場導進最外圈、頂死在看不見的邊界上」原封不動回來，而且沒有任何 log。
            // 不變量寫在測試裡擋得住 CI，擋不住「場景被改名／引用掉了／換成 MeshCollider」的線上版本。
            if (boundary == null)
            {
                Debug.LogError("[VOW] 場景缺少 ArenaBoundary 引用：格點最外圈不會被登記，" +
                               "英雄會被繞牆向量場導去頂死看不見的場地邊界。" +
                               "請執行 VOW/Phase 1/Build Greybox Scene 重建場景。", this);
                return;
            }

            BoxCollider[] boxes = boundary.GetComponentsInChildren<BoxCollider>();
            if (boxes.Length == 0)
            {
                Debug.LogError("[VOW] ArenaBoundary 底下找不到任何 BoxCollider：可站立範圍量不出來，" +
                               "格點最外圈不會被登記，英雄會被繞牆向量場導去頂死看不見的場地邊界。", this);
                return;
            }

            float minX = float.NegativeInfinity, minZ = float.NegativeInfinity;
            float maxX = float.PositiveInfinity, maxZ = float.PositiveInfinity;

            for (int i = 0; i < boxes.Length; i++)
            {
                Bounds bounds = boxes[i].bounds;
                if (bounds.size.x < bounds.size.z) // 沿 Z 延伸的東西兩面牆：內面在 X 上
                {
                    if (bounds.center.x > 0f) maxX = Mathf.Min(maxX, bounds.min.x - _navTuning.BodyRadius);
                    else minX = Mathf.Max(minX, bounds.max.x + _navTuning.BodyRadius);
                }
                else                               // 沿 X 延伸的南北兩面牆：內面在 Z 上
                {
                    if (bounds.center.z > 0f) maxZ = Mathf.Min(maxZ, bounds.min.z - _navTuning.BodyRadius);
                    else minZ = Mathf.Max(minZ, bounds.max.z + _navTuning.BodyRadius);
                }
            }
            _navGrid.SetPlayableBounds(minX, minZ, maxX, maxZ);

            for (int cz = 0; cz < _navGrid.Rows; cz++)
            {
                for (int cx = 0; cx < _navGrid.Columns; cx++)
                {
                    _navGrid.CellCenter(cx, cz, out float x, out float z);
                    if (x >= minX && x <= maxX && z >= minZ && z <= maxZ) continue;
                    _navGrid.StampCell(cx, cz, 1);
                }
            }
        }

        // 任何阻擋物蓋上格點（符印牆立起、測試牆重生、物件重新啟用）時：壓到英雄就把他推到最近的空格
        // （使用者裁定 3）。牆照立，不取消施法。§6 R4：掛在「登記」這個單一入口，不是只掛符印牆的事件。
        private void HandleNavBlockerStamped(CombatTargetBehaviour blocker)
        {
            if (_heroLocomotion == null || blocker == null) return;
            if (!blocker.TryGetNavBlockerBox(out float centerX, out float centerZ, out float normalX, out float normalZ,
                                             out float halfWidth, out float halfThickness))
                return;
            _heroLocomotion.EjectFromBox(centerX, centerZ, normalX, normalZ, halfWidth, halfThickness);
            if (_opponentLocomotion != null && _opponent != null && _opponent.IsEngaged)
                _opponentLocomotion.EjectFromBox(centerX, centerZ, normalX, normalZ, halfWidth, halfThickness);
        }

        // ───────────────────── Phase 2 批 3：護盾／砲台／敵方牆的組裝 ─────────────────────

        private void InitializeBatch3(CombatTargetBehaviour[] targets)
        {
            if (_shield != null) _shield.Initialize(_hero, _projectileTuning);
            if (_shieldBar != null) _shieldBar.Initialize(_shield, _projectileTuning);

            if (_enemyWalls != null && _tuningAsset != null)
                _enemyWalls.Initialize(_tuningAsset.Rune, _projectileTuning, _hero.transform,
                                       CollectWalls(_enemyWallPool, "EnemyWallPool"));

            if (_turret == null) return;

            // 開火方向固定朝木樁（§4-7）。木樁不在場則砲台不開火。
            Transform aimTarget = null;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!(targets[i] is DummyTarget)) continue;
                aimTarget = targets[i].transform;
                break;
            }
            _turret.Initialize(_projectileTuning, _targets, _hero.HeroFaction, aimTarget);
        }

        // ───────────────────── Phase 2 批 4：元素場／名冊／四顆鈕的組裝 ─────────────────────

        // r1 對抗審查 M4（§9 R3）：`CombatTargetRoster.Add` 的回傳值不得丟掉——名冊滿了還硬塞的話，
        // 那個目標就再也吃不到任何元素 AOE／DoT，而所有測試照樣綠（roster 自己的註解寫明「呼叫端要看得到」）。
        // 重複登記同樣回 false，但那不是「丟目標」，所以用 Count/Capacity 把兩種情形分開。
        public void RegisterElementTarget(CombatTargetBehaviour target)
        {
            if (_elementRoster == null || target == null) return;
            if (_elementRoster.Add(target)) return;
            if (_elementRoster.Count < _elementRoster.Capacity) return; // 已經在名冊裡（去重），不是丟掉

            Debug.LogError("[VOW] 元素目標名冊已滿（容量 " + _elementRoster.Capacity + "）：" + target.name
                           + " 沒有登記進去，它不會吃到任何元素 AOE 或燃燒區 DoT。"
                           + "請調大 ElementTuning.TargetRosterCapacity。", this);
        }

        private void InitializeBatch4(CombatTargetBehaviour[] targets)
        {
            _elementCooldowns = new ElementCastCooldowns(_elementTuning);
            // r1 對抗審查 M7（§9 R4）：引用掉了就明說（比照同方法對 ElementZonePool 的處理）。
            // 無聲 return 的話 WATER／FIRE／WIND／ELEM 四顆鈕整組連畫都不畫，三個元素反應完全不會發生。
            if (_elementField == null)
            {
                Debug.LogError("[VOW] 場景缺少 ElementField 引用：WATER／FIRE／WIND／ELEM 四顆除錯鈕整組不會出現，"
                               + "三個元素反應在遊戲內完全不會發生。"
                               + "請執行 VOW/Phase 1/Build Greybox Scene 重建場景。", this);
                return;
            }

            ElementZoneView[] views = CollectElementZoneViews();

            // v0.6.1：反應飄字元件（V061_FEEDBACK_PLAN.md §1）。作法比照 TargetOverheadDisplay，
            // 執行期 AddComponent 一次即可（不是每幀），標籤表要等 tuning 注入後才建得出來。
            _callouts = gameObject.AddComponent<ReactionCalloutDisplay>();
            _callouts.Initialize(_elementTuning);

            _elementField.Initialize(_elementTuning, _elementRoster, _feedback, _sectorTelegraph, views, _callouts);

            // 岩＝既有石牆（使用者裁定 1）：符印牆與除錯鈕生的敵方牆都走 RuneWall.Activate。
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] is RuneWall runeWall) runeWall.SetElementField(_elementField);

            _hero.SetElementField(_elementField, _elementTuning);
            _hero.OnRootedStarted += HandleHeroRooted;
        }

        // 縛足開始的那一幀在英雄頭上跳一次 ROOTED（V061_FEEDBACK_PLAN.md §1）——玩家看的是
        // 英雄本體，不是左上角的 STATE 列（主對話自決，回報時載明）。
        private void HandleHeroRooted()
        {
            if (_callouts != null) _callouts.Show(_hero.transform.position, ElementCalloutLogic.RootedLabelIndex);
        }

        // 區域視覺池由 SceneBuilder 預建（執行期禁止 CreatePrimitive）。引用掉了就明說——
        // 無聲退回空池會讓三個反應在畫面上完全看不見，而所有數值斷言照樣綠。
        private ElementZoneView[] CollectElementZoneViews()
        {
            if (_elementZonePool != null) return _elementZonePool.GetComponentsInChildren<ElementZoneView>(true);

            Debug.LogError("[VOW] 場景缺少 ElementZonePool 引用：元素區域不會有任何視覺，" +
                           "請執行 VOW/Phase 1/Build Greybox Scene 重建場景。", this);
            return new ElementZoneView[0];
        }

        // 水／火＝英雄前方 CastDistanceMeters（r1 HIGH-1 使用者裁定後為 3m）的地面點；
        // 風＝以英雄本體為扇形頂點朝面向（§4-11）。
        private Vector3 ElementCastPoint()
        {
            Vector3 origin = _hero.transform.position;
            Vector3 forward = _hero.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 point = origin + forward * _elementTuning.CastDistanceMeters;
            point.y = origin.y;
            return point;
        }

        // 冷卻閘寫在按鈕委派這一層（不在 ElementField）：冷卻中按下＝**沒有任何事發生**，
        // 不重置冷卻、不扣任何東西（§4-12）。
        private void CastElementWater()
        {
            if (ElementsLocked) return;
            if (!_elementCooldowns.TryBeginCast(ElementCast.Water, Time.time)) return;
            _elementField.CastWater(ElementCastPoint(), (int)_elementFaction);
        }

        private void CastElementFire()
        {
            if (ElementsLocked) return;
            if (!_elementCooldowns.TryBeginCast(ElementCast.Fire, Time.time)) return;
            _elementField.CastFire(ElementCastPoint(), (int)_elementFaction);
        }

        private void CastElementWind()
        {
            if (ElementsLocked) return;
            if (!_elementCooldowns.TryBeginCast(ElementCast.Wind, Time.time)) return;
            _elementField.CastWind(_hero.transform.position, _hero.transform.forward, (int)_elementFaction);
        }

        // ELEM 鈕無冷卻；切換只影響**之後**施放的技能，已成形的區域陣營不變（§4-12）。
        private void ToggleElementFaction()
        {
            if (ElementsLocked) return;
            _elementFaction = _elementFaction == Faction.BlueTeam ? Faction.RedTeam : Faction.BlueTeam;
        }

        private bool IsElementFactionBlue()
        {
            return _elementFaction == Faction.BlueTeam;
        }

        // 從指定的父物件底下收石牆。引用掉了就明說——無聲退回 FindObjectsOfType 會把兩個池又混回一起。
        private RuneWall[] CollectWalls(Transform poolRoot, string expectedName)
        {
            if (poolRoot != null) return poolRoot.GetComponentsInChildren<RuneWall>(true);

            Debug.LogError("[VOW] 場景缺少 " + expectedName + " 引用：石牆池分不出玩家與敵方，" +
                           "請執行 VOW/Phase 1/Build Greybox Scene 重建場景。", this);
            return new RuneWall[0];
        }

        private void SpawnEnemyWall()
        {
            if (ElementsLocked) return;
            if (_enemyWalls != null) _enemyWalls.Spawn();
        }

        private void ToggleTurret()
        {
            if (ElementsLocked) return;
            if (_turret != null) _turret.SetFiring(!_turret.IsFiring);
        }

        private bool IsTurretFiring()
        {
            return _turret != null && _turret.IsFiring;
        }

        private bool IsGridDebugVisible()
        {
            return _navGridDebug != null && _navGridDebug.Visible;
        }

        private void ToggleGridDebug()
        {
            if (_navGridDebug != null) _navGridDebug.Visible = !_navGridDebug.Visible;
        }

        // 目押成功切後搖 → 立即解除上一刀的頓挫幀，滑步動畫不被卡住。
        private void HandleHeroStateChanged(PlayerState oldState, PlayerState newState)
        {
            if (newState == PlayerState.CadenceDashing) _feedback.CancelHitstop();
        }

        // SceneBuilder 會把引用全部接好；這裡只是手動拼場景時的後備。
        private void ResolveMissingReferences()
        {
            if (_hero == null) _hero = FindObjectOfType<HeroController>();
            if (_input == null) _input = FindObjectOfType<PlayerInputService>();
            if (_latency == null) _latency = FindObjectOfType<NetworkLatencySimulator>();
            if (_haptics == null) _haptics = FindObjectOfType<HapticFeedbackService>();
            if (_feedback == null) _feedback = FindObjectOfType<CombatFeedbackService>();
            if (_telegraph == null) _telegraph = FindObjectOfType<SkillTelegraphService>();
            if (_cameraRig == null) _cameraRig = FindObjectOfType<FollowCameraRig>();
            if (_camera == null) _camera = Camera.main;
            if (_hud == null) _hud = FindObjectOfType<DebugHud>();
            if (_hitboxes == null) _hitboxes = FindObjectOfType<HitboxVisualizer>();
            if (_aimPreview == null) _aimPreview = FindObjectOfType<CadenceAimPreview>();
            if (_runeCaster == null) _runeCaster = FindObjectOfType<RuneCaster>();
            if (_runeGhost == null) _runeGhost = FindObjectOfType<RuneGhostPreview>();
            if (_runeButton == null) _runeButton = FindObjectOfType<RuneButtonView>();
            if (_navGridDebug == null) _navGridDebug = FindObjectOfType<NavGridDebugView>();
            if (_turret == null) _turret = FindObjectOfType<TestTurret>();
            if (_enemyWalls == null) _enemyWalls = FindObjectOfType<EnemyWallSpawner>();
            if (_shield == null) _shield = FindObjectOfType<RockShieldBehaviour>();
            if (_shieldBar == null) _shieldBar = FindObjectOfType<HeroShieldBar>();
            if (_elementField == null) _elementField = FindObjectOfType<ElementField>();
            if (_sectorTelegraph == null) _sectorTelegraph = FindObjectOfType<SectorTelegraph>();
            if (_elementZonePool == null)
            {
                GameObject pool = GameObject.Find("ElementZonePool");
                if (pool != null) _elementZonePool = pool.transform;
            }
            if (_arenaBoundary == null)
            {
                GameObject boundary = GameObject.Find("ArenaBoundary");
                if (boundary != null) _arenaBoundary = boundary.transform;
            }
            if (_opponent == null) _opponent = FindObjectOfType<TrainingOpponent>();
            if (_captureBoard == null) _captureBoard = FindObjectOfType<CaptureBoardView>(true); // 預設是關著的
            if (_runeWallPool == null)
            {
                GameObject pool = GameObject.Find("RuneWallPool");
                if (pool != null) _runeWallPool = pool.transform;
            }
            if (_enemyWallPool == null)
            {
                GameObject pool = GameObject.Find("EnemyWallPool");
                if (pool != null) _enemyWallPool = pool.transform;
            }
            // _tuningAsset 是 ScriptableObject 資產、不在場景裡，手動拼場景時沒有 FindObjectOfType 後備，
            // 缺了它符印相關的三個 Initialize 呼叫會被 Start() 的 null 檢查略過（英雄本體不受影響）。
        }
    }
}
