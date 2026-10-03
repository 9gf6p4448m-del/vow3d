using System;
using System.Collections.Generic;
using UnityEngine;
using Vow.Core.Logic;

namespace Vow.Core
{
    // 英雄的組裝點：把輸入事件翻譯成大腦指令，並以 IHeroBodyPort 的身分替大腦執行物理動作。
    // 本類別不含任何節奏判斷——窗口、緩衝、佇列、攻擊週期全部在 HeroCombatBrain（純邏輯、有測試）。
    [RequireComponent(typeof(HeroLocomotion))]
    [RequireComponent(typeof(MicroCadenceMover))]
    public sealed class HeroController : MonoBehaviour, IHeroBodyPort<ICombatTarget>, IAttackHitReceiver
    {
        [SerializeField] private HeroTuningAsset _tuningAsset;
        [SerializeField] private Faction _faction = Faction.BlueTeam;

        private HeroTuningAsset _tuning;
        private HeroLocomotion _locomotion;
        private MicroCadenceMover _mover;
        private PlayerStateMachine _stateMachine;
        private HeroCombatBrain<ICombatTarget> _brain;

        private Func<Vector3> _continuousMove;
        private bool _continuousStarted, _continuousRequested;
        private Func<int> _continuousIntent;
        private int _lastContinuousIntent;
        public void SetContinuousMoveSource(Func<Vector3> source, Func<int> intent = null)
        {
            _continuousMove = source;
            _continuousIntent = intent;
            _lastContinuousIntent = intent != null ? intent() : 0;
            _continuousRequested = false;
            if (_continuousStarted && _locomotion != null) _locomotion.Stop();
            _continuousStarted = false;
        }

        private IPlayerInputService _input;
        private ICombatFeedbackService _feedback;
        private IHapticService _haptics;
        private Transform _cameraTransform;
        private bool _watchdogReported;
        private HeroVitality _vitality;
        private IRockShield _shield;
        private readonly PactAttackWindow _pactAttackWindow = new PactAttackWindow();
        private readonly RaycastHit[] _pierceHits = new RaycastHit[64];
        private readonly HashSet<ICombatTarget> _piercedTargets = new HashSet<ICombatTarget>();
        private PactTalent _pactAttackTalent;
        private ICombatTargetResolver _attackTargetResolver;
        private Func<Vector3, float> _attackDamageMultiplier;
        private CaptureMatchLogic _captureVisibilityMatch;
        private RevealTracker _revealTracker;
        public void SetRevealTracker(RevealTracker tracker) { _revealTracker = tracker; }
        public void TakeDuelDamageFromSide(float amount, DamageType type, int attackerSide)
        {
            float before = Health;
            TakeDuelDamage(amount, type);
            if (Health < before) _revealTracker?.NotifyHit(attackerSide, CaptureMatchLogic.BlueFactionId);
        }
        public void TakeDuelDamageFromUnit(float amount, DamageType type, RevealUnit unit)
        {
            float before = Health;
            TakeDuelDamage(amount, type);
            if (Health < before) _revealTracker?.NotifyUnitHit(unit, CaptureMatchLogic.BlueFactionId);
        }

        // ── Phase 2 批 4：元素場 ──
        // 英雄只認得 Vow.Core 的查詢介面（Vow.Core 不得反向依賴 Vow.Combat）。
        // 兩者為 null 時本類別的行為與 v0.5.0 逐行相同（V4-l）。
        private IElementFieldQuery _elementField;
        private ElementTuning _elementTuning = new ElementTuning();
        private QuicksandStatusLogic _quicksand;
        private bool _wasRooted;

        // V061_FEEDBACK_PLAN.md §1：縛足由 false→true 的那一幀觸發一次（同一個流沙重入不再縛足，
        // QuicksandStatusLogic 的 RootedByZoneId 已經把這條擋掉，這裡只忠實轉達 IsRooted 的邊緣）。
        public event Action OnRootedStarted;

        private static readonly Color KillFlashColor = new Color(1f, 1f, 1f, 0.3f);

        public IPlayerStateMachine StateMachine => _stateMachine;
        public ICadenceMover CadenceMover => _mover;
        public MicroCadenceMover Mover => _mover;
        public Faction HeroFaction => _faction;
        public ICombatTarget CurrentTarget => _brain != null ? _brain.CurrentTarget : null;
        public float CadenceWindowRemainingNormalized => _brain != null ? _brain.CadenceWindowRemainingNormalized : 0f;
        public float AttackRange => _tuning != null ? _tuning.AttackRange : 0f;
        public float AttackDamage => _tuning != null ? _tuning.AttackDamage : 0f;
        // camera-lab 武器灰盒（v0.16.0）：攻擊射程覆寫，只影響 IsTargetInAttackRange；≤ 0＝不覆寫（沿用 HeroTuningAsset.AttackRange）。
        private float _attackRangeOverride;
        public float AttackRangeOverride => _attackRangeOverride;
        // 實際普攻射程：有武器覆寫用覆寫值，否則沿用 HeroTuningAsset.AttackRange（射程圈與裂風矢射線共用，避免寫死 5m）。
        public float EffectiveAttackRange => IsChargedShotEngaged ? _chargedShotRange
            : _attackRangeOverride > 0f ? _attackRangeOverride : AttackRange;
        public void SetAttackRangeOverride(float meters) { _attackRangeOverride = meters > 0f ? meters : 0f; }
        // camera-lab 弓蓄力（2026-10-03，acceptance-bowcharge-20261003.md）：下一次對「這個目標」結算的普攻＝蓄力箭——
        // 射程（只在大腦正打這個目標時生效）、傷害倍率、穿透（沿線可傷目標各吃一次，規則同裂風矢）。結算即清除。
        // 沒有預備時一切行為與原本相同（其他武器、快速射擊都不呼叫 ArmChargedShot）。
        private ICombatTarget _chargedShotTarget;
        private float _chargedShotRange, _chargedShotDamageScale;
        private bool _chargedShotPierce;
        private ICombatTargetResolver _chargedShotResolver;
        // 覆審 r1 H1：預備只活到「這一箭」——大腦接上目標後一旦換掉／清掉目標（搖桿、點別人、失去視野、倒地）即解除；
        // 還沒接上（後搖中排隊、輸入延遲）最多等一個攻擊週期＋前搖保險時限；目標死亡即解除。
        private bool _chargedShotEngaged;
        private float _chargedShotArmedAt;
        public bool HasChargedShot => _chargedShotTarget != null;
        private bool IsChargedShotEngaged => _chargedShotTarget != null && _brain != null
            && ReferenceEquals(_brain.CurrentTarget, _chargedShotTarget);
        public void ArmChargedShot(ICombatTarget target, float rangeMeters, float damageScale, bool pierce,
            ICombatTargetResolver pierceResolver)
        {
            _chargedShotTarget = target;
            _chargedShotRange = rangeMeters;
            _chargedShotDamageScale = damageScale;
            _chargedShotPierce = pierce;
            _chargedShotResolver = pierceResolver;
            _chargedShotEngaged = _brain != null && ReferenceEquals(_brain.CurrentTarget, target);
            _chargedShotArmedAt = Time.time;
        }
        public void DisarmChargedShot()
        {
            _chargedShotTarget = null;
            _chargedShotResolver = null;
            _chargedShotEngaged = false;
        }

        // 每幀（大腦 Tick 之後）：H1 的時間面防線；入口面防線見各 _brain.Command* 呼叫點。
        private void TickChargedShot()
        {
            if (_chargedShotTarget == null) return;
            if (!_chargedShotTarget.IsAlive || _chargedShotTarget.TargetTransform == null) { DisarmChargedShot(); return; }
            bool current = ReferenceEquals(_brain.CurrentTarget, _chargedShotTarget);
            if (current) { _chargedShotEngaged = true; return; }
            if (_chargedShotEngaged
                || Time.time - _chargedShotArmedAt > _tuning.Combat.AttackPeriodSeconds + _tuning.Combat.WindupWatchdogSeconds)
                DisarmChargedShot();
        }
        // camera-lab 武器灰盒（覆審 r1 M1）：比照搖桿起步，原地下一次移動指令清掉普攻目標與失聯追擊記憶（不改狀態機）。
        public void ClearCombatTargetInPlace()
        {
            if (_locomotion.IsVentFlying) return;
            DisarmChargedShot();
            Vector3 p = transform.position;
            ForgetLostTarget();
            _brain.CommandMove(new GroundPoint(p.x, p.y, p.z));
            _locomotion.Stop();
        }
        public float Health => _vitality != null ? _vitality.Health : 100f;
        public float MaxHealth => _vitality != null ? _vitality.MaxHealth : 100f;
        public bool IsAlive => _vitality == null || _vitality.IsAlive;
        public void SetPactCadenceModifiers(bool swiftStep, bool extremeOverclock)
        {
            _mover?.SetPactCadenceModifiers(swiftStep, extremeOverclock);
        }
        // Bootstrap passes None outside an Active capture match. The multiplier is evaluated at each hit.
        public void SetPactAttackTalent(PactTalent talent, ICombatTargetResolver resolver)
        {
            if (talent != PactTalent.WindPiercer && talent != PactTalent.StoneShock)
                talent = PactTalent.None;
            if (_pactAttackTalent != talent) _pactAttackWindow.Clear();
            _pactAttackTalent = talent;
            _attackTargetResolver = resolver;
        }

        public void SetAttackDamageMultiplier(Func<Vector3, float> multiplier)
        {
            _attackDamageMultiplier = multiplier;
        }

        public void SetCaptureVisibilityMatch(CaptureMatchLogic match)
        {
            _captureVisibilityMatch = match;
        }
        public event Action OnKnockedOut;

        // v0.8.0（V080_CAPTURE_PLAN.md R13／E11）：被打中就發，發在扣護盾**之前**——護盾全額吸收也算受傷，
        // 佔領引導據此打斷（只看 HP 有沒有下降的話，護盾會讓英雄挨打時照樣引導）。
        public event Action OnDuelDamaged;

        // v0.8.0（E19）：佔領對局倒地期間整個身體關掉——Renderer 不畫、Collider 不擋路也點不到。
        // 陣列在 Awake 抓一次（模型是場景建置器預建的子物件），切換時零配置。
        private Renderer[] _bodyRenderers;
        private Collider[] _bodyColliders;
        public bool IsBodyHidden { get; private set; }

        public event Action OnAttackWindupStarted;
        public event Action<ICombatTarget> OnAttackHitResolved;
        // camera-lab 弓箭矢（2026-10-03，acceptance-bowline-20261003.md）：普攻每傷到一個目標（直接目標＋穿透／裂風矢沿線）各通知一次，
        // 在 ReceiveDamage 之後、同一幀同一呼叫內依序送出。只是通知，傷害結算時機與數值不變。
        // 第二個參數＝沿線追加目標（滿蓄穿透／裂風矢那一條線）；false＝這次普攻的直接目標（覆審 r1 H1：箭在這一刻生成）。
        public event Action<ICombatTarget, bool> OnAttackDamageDealt;
        // 最近一次直接命中若是滿蓄穿透箭＝它的射程（> 0），否則 0；在送出直接目標的 OnAttackDamageDealt 之前寫入（只是記錄）。
        public float LastHitPierceRangeMeters { get; private set; }

        private void Awake()
        {
            _tuning = _tuningAsset != null ? _tuningAsset : ScriptableObject.CreateInstance<HeroTuningAsset>();

            _locomotion = GetComponent<HeroLocomotion>();
            _mover = GetComponent<MicroCadenceMover>();
            _locomotion.Configure(_tuning.MoveSpeed, _tuning.TurnSpeedDegreesPerSecond, _tuning.BodyRadius);
            _mover.Configure(_tuning.Combat);

            _stateMachine = new PlayerStateMachine();
            _stateMachine.OnTransitionRejected += HandleTransitionRejected;

            _brain = new HeroCombatBrain<ICombatTarget>(_stateMachine, this, _tuning.Combat);
            _brain.OnAttackWindupStarted += HandleWindupStarted;
            _brain.OnAttackHitResolved += HandleHitResolved;
            _brain.OnWindupWatchdogFired += HandleWatchdogFired;
            _mover.OnDashExecuted += HandleDashExecuted;

            _quicksand = new QuicksandStatusLogic(_elementTuning);
            _vitality = new HeroVitality(new DuelTuning().HeroHealth);

            _bodyRenderers = GetComponentsInChildren<Renderer>(true);
            _bodyColliders = GetComponentsInChildren<Collider>(true);
        }

        // 佔領對局的倒地顯示切換（E19）。單挑模式從不呼叫它。
        public void SetBodyHidden(bool hidden)
        {
            IsBodyHidden = hidden;
            for (int i = 0; i < _bodyRenderers.Length; i++)
                if (_bodyRenderers[i] != null) _bodyRenderers[i].enabled = !hidden;
            for (int i = 0; i < _bodyColliders.Length; i++)
                if (_bodyColliders[i] != null) _bodyColliders[i].enabled = !hidden;
        }

        public void ConfigureDuel(DuelTuning tuning, IRockShield shield)
        {
            _vitality = new HeroVitality(tuning.HeroHealth);
            _shield = shield;
        }

        public void TakeDuelDamage(float amount)
        {
            TakeDuelDamage(amount, DamageType.Physical);
        }

        public void TakeDuelDamage(float amount, DamageType type)
        {
            if (!IsAlive || amount <= 0f) return;
            OnDuelDamaged?.Invoke();
            // v0.10.0（V0100_SANCTUARY_PLAN.md E3／E4、Q14）：聖所減傷排在打斷之後、護盾之前——護盾吸收的是減傷後的量。
            // 先乘整數再除 100（20／60／100 在 P＝85 時精確得 17／51／85）；P＝100 時不做乘除，傷害與 v0.9.1 逐位相同。
            if (type != DamageType.True && _damageTakenPercent != 100)
                amount = amount * _damageTakenPercent / 100f;
            if (_shield != null) amount = _shield.Absorb(amount);
            if (!_vitality.TakeDamage(amount)) return;
            CancelCombatForDuel();
            OnKnockedOut?.Invoke();
        }

        public void CancelCombatForDuel()
        {
            DisarmChargedShot();
            _pactAttackWindow.Clear();
            _brain.ResetForRound();
            _mover.ResetForRound();
            _locomotion.Stop();
        }

        public void ResetForDuel(Vector3 spawnPosition)
        {
            CancelCombatForDuel();
            _quicksand.Reset();
            _wasRooted = false;
            _locomotion.SetMovementLocked(false);
            _rageMultiplier = 1f;
            _locomotion.SetSpeedMultiplier(1f);
            _locomotion.WarpTo(spawnPosition);
            _vitality.Restore();
            if (_shield != null) _shield.Clear();
        }

        // 由 Phase1Bootstrap 注入（批 4）。tuning 傳全場共用的那一份，數值只有一個來源。
        public void SetElementField(IElementFieldQuery field, ElementTuning tuning)
        {
            _elementField = field;
            if (tuning != null)
            {
                _elementTuning = tuning;
                _quicksand = new QuicksandStatusLogic(_elementTuning);
            }
            if (field != null) return;

            // 拔掉元素場：縛足／減速不得留在身上（V7 點名 ②——IsMovementLocked 卡在 true＝玩家永久卡死）。
            if (_quicksand != null) _quicksand.Reset();
            if (_locomotion == null) return;
            _locomotion.SetMovementLocked(false);
            _locomotion.SetSpeedMultiplier(1f);
        }

        // 這個英雄此刻在哪個敵對流沙裡（測試用；-1 ＝不在任何敵對流沙內）。
        public int HostileQuicksandZoneId => _elementField != null
            ? _elementField.FindHostileQuicksandId(transform.position, (int)_faction)
            : -1;

        public bool IsRooted => _quicksand != null && _quicksand.IsRooted;
        public float QuicksandSpeedMultiplier => _quicksand != null ? _quicksand.SpeedMultiplier : 1f;

        // v0.9.0 E17：劣勢狂怒的移速倍率。實際倍率＝流沙倍率 × 狂怒倍率，只在 TickQuicksand 這一處合成
        //（TickQuicksand 每幀都會覆寫 SetSpeedMultiplier，R2）。由組裝根每幀依 ICaptureMatchView 寫入；
        // ResetForDuel 會把它歸 1（R3），所以復活後靠組裝根的下一次寫入恢復。單挑模式從不呼叫，恆為 1f。
        private float _rageMultiplier = 1f;
        public float RageSpeedMultiplier => _rageMultiplier;
        public void SetRageSpeedMultiplier(float multiplier) { _rageMultiplier = multiplier; }

        // v0.10.0（V0100_SANCTUARY_PLAN.md E3～E5）：聖所受傷百分比（85～100），在 TakeDuelDamage 內套用。
        // 由組裝根每幀依 CaptureMatchLogic 寫入；非 Active（Off／Lobby／Ended）與開局點擊當幀一律是 100。單挑恆為 100。
        private int _damageTakenPercent = 100;
        public int DamageTakenPercent => _damageTakenPercent;
        public void SetDamageTakenPercent(int percent) { _damageTakenPercent = percent; }

        // 由 Phase1Bootstrap 注入依賴（不在這裡 Find，任何一項都可以換成測試替身）。
        public void Initialize(IPlayerInputService input, ICombatFeedbackService feedback, Camera viewCamera,
            IHapticService haptics = null)
        {
            Unsubscribe();
            _input = input;
            _feedback = feedback;
            _haptics = haptics;
            _cameraTransform = viewCamera != null ? viewCamera.transform : null;

            if (_input == null) return;
            _input.OnMoveDestinationSelected += HandleMoveSelected;
            _input.OnCombatTargetSelected += HandleTargetSelected;
            _input.OnCadenceVectorFlicked += HandleCadenceFlick;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_input == null) return;
            _input.OnMoveDestinationSelected -= HandleMoveSelected;
            _input.OnCombatTargetSelected -= HandleTargetSelected;
            _input.OnCadenceVectorFlicked -= HandleCadenceFlick;
            _input = null;
        }

        private void Update()
        {
            if (!IsAlive)
            {
                DisarmChargedShot();
                ForgetLostTarget();
                if (_locomotion.IsVentFlying) _locomotion.Step(Time.deltaTime);
                return;
            }
            float dt = Time.deltaTime;
            TickQuicksand(dt);
            Vector3 continuous = _continuousMove != null ? _continuousMove() : Vector3.zero;
            bool pushing = continuous.sqrMagnitude > 0.0001f && !_locomotion.IsMovementLocked;
            int intent = _continuousIntent != null ? _continuousIntent() : 0;
            if (intent != _lastContinuousIntent) { _continuousRequested = pushing; _lastContinuousIntent = intent; }
            if (!pushing) _continuousRequested = false;
            if (pushing && _continuousRequested && _stateMachine.CanMove)
            {
                Vector3 p = transform.position;
                ForgetLostTarget();
                DisarmChargedShot();
                _brain.CommandMove(new GroundPoint(p.x, p.y, p.z));
                _locomotion.Stop();
                _continuousStarted = true;
                _continuousRequested = false;
            }
            if (!pushing && _continuousStarted)
            {
                _locomotion.Stop();
                _continuousStarted = false;
            }
            // camera-lab 自由滑步（大腦不在 CadenceDashing 卻有滑步在走）期間只套滑步位移，不疊步行／導航。
            // 命中連動滑步時大腦恆為 CadenceDashing，這個條件恆假，原路徑不變。
            bool freeDashing = _mover.IsDashing && _brain.State != PlayerState.CadenceDashing && !_locomotion.IsVentFlying;
            if (!freeDashing)
            {
                if (pushing && _continuousStarted && (_brain.State == PlayerState.Idle || _brain.State == PlayerState.Moving))
                    _locomotion.StepContinuous(continuous, dt);
                else _locomotion.Step(dt);
            }
            _mover.Step(dt);
            _brain.Tick(dt);
            TickLostTargetMemory();
            TickChargedShot();
        }

        // 失去視野的追擊記憶（camera-lab K）：目標還活著、只是看不見（CanEngage=false，例如崖台視野 8m、迷霧、蒸氣）時，
        // 大腦會清掉目標（HeroCombatBrain.EngageCurrentTarget）。改為走到「最後看見的位置」——導航會自己走斜坡——
        // 途中同一目標重新看得見就接回追打。看不見期間不追即時位置（迷霧公平）。
        // 抵達、玩家任何新指令、英雄或目標死亡即取消。
        private ICombatTarget _trackedTarget;
        private Vector3 _trackedLastSeen;
        private ICombatTarget _lostTarget;

        public ICombatTarget LostTargetForTest => _lostTarget;

        private void TickLostTargetMemory()
        {
            ICombatTarget current = _brain.CurrentTarget;
            if (current != null)
            {
                _lostTarget = null;
                _trackedTarget = current;
                // 只在「此刻看得見」時記位置：後搖／滑步期間大腦不驗目標，目標可能已入霧或出視野仍掛著（審查 r1 F2）。
                if (current.TargetTransform != null && CanEngage(current)) _trackedLastSeen = current.TargetTransform.position;
                return;
            }
            if (_trackedTarget != null)
            {
                ICombatTarget lost = _trackedTarget;
                _trackedTarget = null;
                if (lost.IsAlive && lost.TargetTransform != null && lost.CanBeTargetedBy(_faction) && !CanEngage(lost)
                    && _brain.State == PlayerState.Idle)
                {
                    _lostTarget = lost;
                    DisarmChargedShot();
                    _brain.CommandMove(new GroundPoint(_trackedLastSeen.x, _trackedLastSeen.y, _trackedLastSeen.z));
                    return;
                }
            }
            if (_lostTarget == null) return;
            if (!_lostTarget.IsAlive || _brain.State != PlayerState.Moving) { _lostTarget = null; return; }
            if (CanEngage(_lostTarget))
            {
                ICombatTarget target = _lostTarget;
                _lostTarget = null;
                DisarmChargedShot();
                _brain.CommandAttack(target);
            }
        }

        private void ForgetLostTarget()
        {
            _trackedTarget = null;
            _lostTarget = null;
        }

        // 泥濘流沙（GDD 圍欄九）：進入（或成形時已在內）起算 1.2s 禁位移，之後只要還在區內就是 ×0.65。
        // 每幀無條件重算並推給 locomotion —— 流沙終止、被池擠掉、被救援／爆沸、英雄走出去，
        // 四種情形都會讓 `FindHostileQuicksandId` 回 -1，QuicksandStatusLogic 當幀就把縛足與減速清乾淨。
        // 沒有元素場時 zoneId 恆為 -1、倍率恆為 1f、鎖恆為 false（V4-l 的「逐值不變」靠這一條）。
        private void TickQuicksand(float dt)
        {
            int zoneId = _elementField != null
                ? _elementField.FindHostileQuicksandId(transform.position, (int)_faction)
                : -1;

            _quicksand.Tick(dt, zoneId);
            _locomotion.SetMovementLocked(_quicksand.IsRooted);
            _locomotion.SetSpeedMultiplier(_quicksand.SpeedMultiplier * _rageMultiplier);

            bool isRootedNow = _quicksand.IsRooted;
            if (isRootedNow && !_wasRooted) OnRootedStarted?.Invoke();
            _wasRooted = isRootedNow;
        }

        // 全英雄唯一的「這個目標打不打得到」（§2「鎖定判準的收斂」）：陣營、佔領視野、蒸氣遮蔽。
        // 生產呼叫點 N＝2（點擊當下的 HandleTargetSelected、持續驗證的 IsTargetValid），兩個都走這裡，
        // 涵蓋 2/2。`ICombatTarget.CanBeTargetedBy(Faction)` 的簽章一字不動——它拿不到攻擊者座標，
        // 而蒸氣規則②（同一團霧裡的攻擊者照樣打得到）需要。
        public bool CanEngage(ICombatTarget target)
        {
            if (target == null || !target.CanBeTargetedBy(_faction)) return false;
            if (target.TargetTransform == null) return !CaptureVisibilityLogic.AppliesTo(_captureVisibilityMatch);
            if (target is IGlobalObjectiveVisibility objective && objective.IsGloballyVisibleTo(_faction)) return true;
            Vector3 targetPosition = target.TargetTransform.position;
            if (CaptureVisibilityLogic.AppliesTo(_captureVisibilityMatch))
            {
                Vector3 heroPosition = transform.position;
                if (!CaptureVisibilityLogic.CanSee(_captureVisibilityMatch,
                    target.TargetFaction == Faction.RedTeam || target.TargetFaction == Faction.BlueTeam ? _revealTracker : null,
                    target is IFactionOwned ownedTarget && ownedTarget.OwnerFaction == Faction.RedTeam
                        && target is IGlobalObjectiveVisibility ? RevealUnit.RedBehemoth : RevealUnit.RedOpponent, (int)_faction,
                    heroPosition.x, heroPosition.z, !IsAlive, targetPosition.x, targetPosition.z)) return false;
                if (CaptureVisibilityLogic.HasTrueVision(_captureVisibilityMatch, (int)_faction,
                    targetPosition.x, targetPosition.z)) return true;
            }
            if (_elementField == null) return true;
            bool revealed = target is IConcealable concealable && concealable.IsRevealed;
            return !_elementField.IsConcealedFrom(targetPosition, revealed, transform.position);
        }

        // ───────────────────────── 輸入 → 指令 ─────────────────────────

        private void HandleMoveSelected(Vector3 destination)
        {
            if (_locomotion.IsVentFlying) return;
            ForgetLostTarget();
            DisarmChargedShot();
            _brain.CommandMove(new GroundPoint(destination.x, destination.y, destination.z));
        }

        private void HandleTargetSelected(ICombatTarget target)
        {
            if (_locomotion.IsVentFlying) return;
            if (!CanEngage(target)) return;   // 批 4：收斂成單一判準（陣營＋蒸氣遮蔽）
            ForgetLostTarget();
            if (!ReferenceEquals(target, _chargedShotTarget)) DisarmChargedShot();   // 弓放開送出的就是預備目標本身
            // 目標tap接手導航；仍按著但沒新操作的搖桿不搶走追擊。
            _continuousStarted = _continuousRequested = false;
            _brain.CommandAttack(target);
            // 覆審 r2：同一幀就被打斷（放開＋DASH）也要算「接上過」，下一幀每幀防線才會解除。
            if (_chargedShotTarget != null && ReferenceEquals(_brain.CurrentTarget, _chargedShotTarget)) _chargedShotEngaged = true;
        }

        // 螢幕向量 → 世界 XZ 方向（以鏡頭水平朝向為基準，玩家往螢幕哪邊彈，角色就往畫面上的那邊滑）。
        private void HandleCadenceFlick(Vector2 screenDirection)
        {
            if (_locomotion.IsVentFlying) return;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (_cameraTransform != null)
            {
                forward = _cameraTransform.forward;
                right = _cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;
                if (forward.sqrMagnitude < 1e-6f) forward = _cameraTransform.up; // 純俯視鏡頭
                forward.y = 0f;
                forward.Normalize();
                right.Normalize();
            }

            Vector3 world = right * screenDirection.x + forward * screenDirection.y;
            _brain.CommandFlick(world.x, world.z);
        }

        // 動畫事件 OnAttackHit() 的落點（經 HeroAnimationDriver 轉送）。
        public void NotifyAttackHit()
        {
            _brain.NotifyAttackHit();
        }

        // ───────────────────────── IHeroBodyPort ─────────────────────────

        public bool HasArrivedAtDestination => _locomotion.HasArrived;
        public bool IsCadenceDashComplete => !_mover.IsDashing;

        public bool IsTargetValid(ICombatTarget target)
        {
            return target != null && target.IsAlive && target.TargetTransform != null
                   && CanEngage(target);   // 批 4：與點擊當下同一個判準
        }

        public bool IsTargetInAttackRange(ICombatTarget target)
        {
            if (target == null) return false;
            Transform targetTransform = target.TargetTransform;
            if (targetTransform == null) return false;

            Vector3 offset = targetTransform.position - transform.position;
            offset.y = 0f;
            float baseRange = EffectiveAttackRange;
            return CanyonRules.InAttackRange(baseRange, transform.position.x, transform.position.z,
                targetTransform.position.x, targetTransform.position.z, _locomotion.TerrainQuery);
        }

        public void MoveTo(GroundPoint destination)
        {
            _locomotion.MoveTo(new Vector3(destination.X, destination.Y, destination.Z));
        }

        public void ChaseTarget(ICombatTarget target)
        {
            _locomotion.Chase(target.TargetTransform);
        }

        public void StopMoving()
        {
            _locomotion.Stop();
        }

        public void FaceTarget(ICombatTarget target)
        {
            Transform targetTransform = target.TargetTransform;
            if (targetTransform != null) _locomotion.FaceTowards(targetTransform.position);
        }

        // 打擊反饋金字塔（GDD §肆-2）：普通平 A＝頓挫＋微震＋輕震覺；斬殺＝再加瞬時閃白與焦痕貼花；破牆＝重震＋重震覺＋地裂貼花。
        public void ResolveAttackHit(ICombatTarget target)
        {
            // Capture direction before the direct hit can destroy or deactivate the target.
            Vector3 direction = target.TargetTransform != null
                ? target.TargetTransform.position - transform.position : Vector3.zero;
            float damage = _tuning.AttackDamage * (_attackDamageMultiplier != null
                ? _attackDamageMultiplier(transform.position) : 1f);
            bool charged = _chargedShotTarget != null && ReferenceEquals(target, _chargedShotTarget);
            bool chargedPierce = false;
            float chargedRange = 0f, chargedScale = 1f;
            ICombatTargetResolver chargedResolver = null;
            if (charged)
            {
                chargedRange = _chargedShotRange;
                chargedScale = _chargedShotDamageScale;
                chargedPierce = _chargedShotPierce;
                chargedResolver = _chargedShotResolver;
                DisarmChargedShot();
                damage *= chargedScale;
            }
            bool empowered = _pactAttackTalent != PactTalent.None && _pactAttackWindow.Consume(_brain.Clock);
            target.ReceiveDamage(damage, DamageType.Physical, gameObject);
            LastHitPierceRangeMeters = chargedPierce ? chargedRange : 0f;
            OnAttackDamageDealt?.Invoke(target, false);
            if (chargedPierce)
                ResolveLinePierce(target, direction, chargedResolver, chargedRange, chargedScale);   // 已含裂風矢那一條線
            else if (empowered && _pactAttackTalent == PactTalent.WindPiercer)
            {
                if (charged) ResolveLinePierce(target, direction, _attackTargetResolver, chargedRange, 1f);   // 覆審 r1 L4
                else ResolveWindPierce(target, direction);
            }
            if (empowered && _pactAttackTalent == PactTalent.StoneShock && target is ICaptureStunnable stunnable)
                stunnable.ApplyCaptureStun(0.5f);

            bool killed = !target.IsAlive;
            bool wallBroken = killed && target.TargetFaction == Faction.DestructibleWall;
            if (_haptics != null) _haptics.Notify(wallBroken ? HapticCue.WallBreak : HapticCue.BasicAttackHit);
            if (_feedback == null) return;

            _feedback.TriggerHitstop(_tuning.HitstopMilliseconds);
            Transform targetTransform = target.TargetTransform;

            if (wallBroken)
            {
                _feedback.RequestCameraShake(_tuning.WallBreakTrauma, 0.45f);
                if (targetTransform != null) _feedback.SpawnGroundDecal(targetTransform.position, DecalType.VoidRupture);
                return;
            }

            _feedback.RequestCameraShake(_tuning.BasicAttackTrauma, 0.15f);
            if (!killed) return;

            _feedback.TriggerScreenFlash(KillFlashColor, 60f);
            if (targetTransform != null) _feedback.SpawnGroundDecal(targetTransform.position, DecalType.ScorchCrater);
        }

        private void ResolveWindPierce(ICombatTarget directTarget, Vector3 direction)
        {
            ResolveLinePierce(directTarget, direction, _attackTargetResolver, EffectiveAttackRange, 1f);
        }

        // 裂風矢與滿蓄弓箭共用：沿英雄→直接目標方向、射程內的射線，每個可傷目標（不含直接目標、己方石牆、同陣營）各吃一次。
        private void ResolveLinePierce(ICombatTarget directTarget, Vector3 direction, ICombatTargetResolver resolver,
            float rangeMeters, float damageScale)
        {
            if (resolver == null || direction.sqrMagnitude < 1e-6f) return;
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            int count = Physics.RaycastNonAlloc(origin, direction.normalized, _pierceHits, rangeMeters,
                                                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count >= _pierceHits.Length)
            {
                Debug.LogWarning("[VOW] 裂風矢射線緩衝已滿，取消這次貫穿。", this);
                return;
            }
            _piercedTargets.Clear();
            _piercedTargets.Add(directTarget);
            for (int i = 0; i < count; i++)
            {
                if (!resolver.TryResolve(_pierceHits[i].collider, out ICombatTarget target)
                    || target == null || !target.IsAlive || !_piercedTargets.Add(target)
                    || !target.CanBeTargetedBy(_faction)) continue;
                if (target.TargetFaction == Faction.DestructibleWall
                    && target is IFactionOwned owned && owned.OwnerFaction == _faction) continue;
                float damage = _tuning.AttackDamage * (_attackDamageMultiplier != null
                    ? _attackDamageMultiplier(transform.position) : 1f) * damageScale;
                target.ReceiveDamage(damage, DamageType.Physical, gameObject);
                OnAttackDamageDealt?.Invoke(target, true);
            }
        }

        public bool TryBeginCadenceDash(float worldDirX, float worldDirZ)
        {
            // 縛足期間不得滑步，而且**不消耗充能**（附加規則，不是位移防線本體——防線在 ApplyDisplacement）。
            if (_locomotion.IsVentFlying || (_quicksand != null && _quicksand.IsRooted)) return false;
            return _mover.TryExecuteCadenceDash(new Vector3(worldDirX, 0f, worldDirZ));
        }

        // camera-lab 主動滑步鈕（GDD §貳.2 普攻後搖綁定，v0.15.0 修訂條）：分流全在 ActiveDashLogic，充能與衰減與命中連動共用 _mover。
        // 縛足／噴口飛行／已在滑步中一律不動任何狀態（含不打斷前搖）。
        public ActiveDashOutcome TryActiveDash(Vector3 worldDirection)
        {
            if (!IsAlive || _locomotion.IsVentFlying || (_quicksand != null && _quicksand.IsRooted) || _mover.IsDashing)
                return ActiveDashOutcome.Rejected;
            Vector3 p = transform.position;
            return ActiveDashLogic.Execute(_brain, this, _mover.CurrentCharges, worldDirection.x, worldDirection.z,
                new GroundPoint(p.x, p.y, p.z));
        }

        // ───────────────────────── 大腦事件 ─────────────────────────

        private void HandleDashExecuted(float distance)
        {
            if (_pactAttackTalent != PactTalent.None) _pactAttackWindow.Arm(_brain.Clock);
            if (_haptics != null) _haptics.Notify(HapticCue.CadenceDash);
        }

        private void HandleWindupStarted()
        {
            OnAttackWindupStarted?.Invoke();
        }

        private void HandleHitResolved(ICombatTarget target)
        {
            OnAttackHitResolved?.Invoke(target);
        }

        private void HandleTransitionRejected(PlayerState from, PlayerState to)
        {
            Debug.LogError("[VOW] 非法狀態轉換被拒絕：" + from + " → " + to, this);
        }

        private void HandleWatchdogFired()
        {
            if (_watchdogReported) return;
            _watchdogReported = true;
            Debug.LogError("[VOW] 前搖逾時仍未收到動畫事件 OnAttackHit()，已由保險機制強制結算。" +
                           "請檢查 Attack 動畫切片是否掛了 OnAttackHit 事件（執行 VOW/Phase 1/Build Greybox Scene 會自動補上）。", this);
        }
    }
}
