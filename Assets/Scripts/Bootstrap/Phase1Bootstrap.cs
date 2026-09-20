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
            }
            if (_aimPreview != null) _aimPreview.Initialize(_hero, _input, _telegraph, _camera);
        }

        private void OnDestroy()
        {
            if (_hero != null && _feedback != null) _hero.StateMachine.OnStateChanged -= HandleHeroStateChanged;
            if (_hero != null) _hero.OnRootedStarted -= HandleHeroRooted;
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
            if (!_elementCooldowns.TryBeginCast(ElementCast.Water, Time.time)) return;
            _elementField.CastWater(ElementCastPoint(), (int)_elementFaction);
        }

        private void CastElementFire()
        {
            if (!_elementCooldowns.TryBeginCast(ElementCast.Fire, Time.time)) return;
            _elementField.CastFire(ElementCastPoint(), (int)_elementFaction);
        }

        private void CastElementWind()
        {
            if (!_elementCooldowns.TryBeginCast(ElementCast.Wind, Time.time)) return;
            _elementField.CastWind(_hero.transform.position, _hero.transform.forward, (int)_elementFaction);
        }

        // ELEM 鈕無冷卻；切換只影響**之後**施放的技能，已成形的區域陣營不變（§4-12）。
        private void ToggleElementFaction()
        {
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
            if (_enemyWalls != null) _enemyWalls.Spawn();
        }

        private void ToggleTurret()
        {
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
