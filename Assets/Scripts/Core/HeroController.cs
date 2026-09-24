using System;
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

        private IPlayerInputService _input;
        private ICombatFeedbackService _feedback;
        private IHapticService _haptics;
        private Transform _cameraTransform;
        private bool _watchdogReported;
        private HeroVitality _vitality;
        private IRockShield _shield;

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
        public float Health => _vitality != null ? _vitality.Health : 100f;
        public float MaxHealth => _vitality != null ? _vitality.MaxHealth : 100f;
        public bool IsAlive => _vitality == null || _vitality.IsAlive;
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
            if (!IsAlive || amount <= 0f) return;
            OnDuelDamaged?.Invoke();
            if (_shield != null) amount = _shield.Absorb(amount);
            if (!_vitality.TakeDamage(amount)) return;
            CancelCombatForDuel();
            OnKnockedOut?.Invoke();
        }

        public void CancelCombatForDuel()
        {
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
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            TickQuicksand(dt);
            _locomotion.Step(dt);
            _mover.Step(dt);
            _brain.Tick(dt);
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
            _locomotion.SetSpeedMultiplier(_quicksand.SpeedMultiplier);

            bool isRootedNow = _quicksand.IsRooted;
            if (isRootedNow && !_wasRooted) OnRootedStarted?.Invoke();
            _wasRooted = isRootedNow;
        }

        // 全英雄唯一的「這個目標打不打得到」（§2「鎖定判準的收斂」）：陣營校驗 ＋ 蒸氣遮蔽。
        // 生產呼叫點 N＝2（點擊當下的 HandleTargetSelected、持續驗證的 IsTargetValid），兩個都走這裡，
        // 涵蓋 2/2。`ICombatTarget.CanBeTargetedBy(Faction)` 的簽章一字不動——它拿不到攻擊者座標，
        // 而蒸氣規則②（同一團霧裡的攻擊者照樣打得到）需要。
        public bool CanEngage(ICombatTarget target)
        {
            if (target == null || !target.CanBeTargetedBy(_faction)) return false;
            if (_elementField == null || target.TargetTransform == null) return true;
            bool revealed = target is IConcealable concealable && concealable.IsRevealed;
            return !_elementField.IsConcealedFrom(target.TargetTransform.position, revealed, transform.position);
        }

        // ───────────────────────── 輸入 → 指令 ─────────────────────────

        private void HandleMoveSelected(Vector3 destination)
        {
            _brain.CommandMove(new GroundPoint(destination.x, destination.y, destination.z));
        }

        private void HandleTargetSelected(ICombatTarget target)
        {
            if (!CanEngage(target)) return;   // 批 4：收斂成單一判準（陣營＋蒸氣遮蔽）
            _brain.CommandAttack(target);
        }

        // 螢幕向量 → 世界 XZ 方向（以鏡頭水平朝向為基準，玩家往螢幕哪邊彈，角色就往畫面上的那邊滑）。
        private void HandleCadenceFlick(Vector2 screenDirection)
        {
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
            return offset.sqrMagnitude <= _tuning.AttackRange * _tuning.AttackRange;
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
            target.ReceiveDamage(_tuning.AttackDamage, DamageType.Physical, gameObject);

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

        public bool TryBeginCadenceDash(float worldDirX, float worldDirZ)
        {
            // 縛足期間不得滑步，而且**不消耗充能**（附加規則，不是位移防線本體——防線在 ApplyDisplacement）。
            if (_quicksand != null && _quicksand.IsRooted) return false;
            return _mover.TryExecuteCadenceDash(new Vector3(worldDirX, 0f, worldDirZ));
        }

        // ───────────────────────── 大腦事件 ─────────────────────────

        private void HandleDashExecuted(float distance)
        {
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
