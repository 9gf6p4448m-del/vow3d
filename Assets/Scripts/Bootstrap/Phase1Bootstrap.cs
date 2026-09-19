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

        private readonly ColliderTargetRegistry _targets = new ColliderTargetRegistry();

        // ── Phase 2 批 2：0.5m 阻擋格點。全場唯一一份，牆登記進來、英雄從這裡拿繞牆方向 ──
        private readonly NavGridTuning _navTuning = new NavGridTuning();
        private BlockGrid _navGrid;
        private GridNavigator _navigator;
        private HeroLocomotion _heroLocomotion;
        private System.Action<CombatTargetBehaviour> _navStampedHandler; // 只建一次，執行期零配置

        public BlockGrid NavGrid => _navGrid;
        public GridNavigator Navigator => _navigator;

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
            for (int i = 0; i < targets.Length; i++) _targets.Register(targets[i]);

            BuildNavGrid(targets);

            _input.Initialize(_targets, _camera);

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
            _hero.Initialize(heroInput, _feedback, _camera, _haptics);

            // 符印石牆：極速施放／鬆手成牆走延遲注入層（heroInput／heroRuneInput，同英雄本體）；
            // 虛影與按鈕是純本地回饋，直接訂閱 _input，不經延遲（計畫書 §4 假設 11）。
            if (_runeCaster != null && _tuningAsset != null)
            {
                RuneWall[] runeWallPool = FindObjectsOfType<RuneWall>();
                _runeCaster.Initialize(heroInput, heroRuneInput, _hero.transform, _camera, _tuningAsset.Rune, runeWallPool, _hero.HeroFaction);
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
                if (_navGridDebug != null) _hud.Initialize(_hero, _input, _hitboxes, _latency, IsGridDebugVisible, ToggleGridDebug);
                else _hud.Initialize(_hero, _input, _hitboxes, _latency);
            }
            if (_aimPreview != null) _aimPreview.Initialize(_hero, _input, _telegraph, _camera);
        }

        private void OnDestroy()
        {
            if (_hero != null && _feedback != null) _hero.StateMachine.OnStateChanged -= HandleHeroStateChanged;
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
            if (_arenaBoundary == null) return;

            float minX = float.NegativeInfinity, minZ = float.NegativeInfinity;
            float maxX = float.PositiveInfinity, maxZ = float.PositiveInfinity;

            BoxCollider[] boxes = _arenaBoundary.GetComponentsInChildren<BoxCollider>();
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
            if (_arenaBoundary == null)
            {
                GameObject boundary = GameObject.Find("ArenaBoundary");
                if (boundary != null) _arenaBoundary = boundary.transform;
            }
            // _tuningAsset 是 ScriptableObject 資產、不在場景裡，手動拼場景時沒有 FindObjectOfType 後備，
            // 缺了它符印相關的三個 Initialize 呼叫會被 Start() 的 null 檢查略過（英雄本體不受影響）。
        }
    }
}
