using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 《VOW 誓約》P0 核心控制器：微彈指節奏走A (Micro-Flick Cadence Controller)
/// 實裝規則：
/// 1. 命中瞬間 100ms 目押窗口 (Just-Frame)。
/// 2. 原地微彈 15~30px 觸發 360 度微衝刺 (PvP 限定)。
/// 3. PvE (小兵野怪) 嚴格禁用微衝刺，僅允許普通取消後搖。
/// 4. 防無腦狂戳 (Anti-Mashing)：0.3 秒內點擊超過 2 次觸發 0.25 秒卡刀硬直。
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MicroFlickCadenceController : MonoBehaviour
{
    [Header("基礎戰鬥數值")]
    [SerializeField] private float attackRange = 5.5f;
    [SerializeField] private float attackRate = 1.0f;           // 每秒普攻次數
    [SerializeField] private float windupRatio = 0.25f;          // 前搖佔比 (0.25s)
    [SerializeField] private float baseDamage = 85f;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private LayerMask groundLayer;

    [Header("P0 微彈指目押配置")]
    [SerializeField] private float justFrameWindow = 0.12f;      // 120ms 寬容目押窗口
    [SerializeField] private float microDashDistance = 1.2f;     // 微衝刺距離 (1.2米)
    [SerializeField] private float minFlickPixels = 15f;         // 判定為微彈的最短像素
    [SerializeField] private float maxFlickPixels = 60f;         // 微彈上限 (超過則忽略，防誤觸)

    [Header("防狂戳 (Anti-Mashing) 配置")]
    [SerializeField] private float mashingThresholdTime = 0.3f;  // 狂點檢測窗口
    [SerializeField] private int maxAllowedClicks = 2;           // 最大允許次數
    [SerializeField] private float jamStumbleDuration = 0.25f;   // 卡刀硬直時間

    // 組件引用與狀態
    private NavMeshAgent agent;
    private Camera mainCam;
    private CombatTarget currentTarget;
    private bool isInJustFrameWindow = false;
    private bool isJammed = false;
    private float nextAttackTime = 0f;
    private Coroutine attackRoutine;

    // 手指觸控/微彈檢測
    private Vector2 touchDownPos;
    private float touchDownTime;
    private bool isTrackingTouch = false;

    // 狂戳計數器
    private int recentClickCount = 0;
    private float lastClickTime = 0f;

    // 事件回調 (供特效、音效與震動外掛監聽)
    public event Action OnPerfectCadenceDash;
    public event Action OnCadenceJam;

    private float AttackPeriod => 1f / attackRate;
    private float WindupDuration => AttackPeriod * windupRatio;
    private float BackswingDuration => AttackPeriod * (1f - windupRatio);

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        mainCam = Camera.main;
    }

    private void Update()
    {
        if (isJammed) return;

        HandleInputDetection();
        UpdateCombatTargeting();
    }

    /// <summary>
    /// 輸入解析：兼顧點擊目標、點地移動與「原地微彈 (Micro-Flick)」
    /// </summary>
    private void HandleInputDetection()
    {
        // 1. 手指按下
        if (Input.GetMouseButtonDown(0))
        {
            RecordClickForAntiMashing();
            if (isJammed) return;

            touchDownPos = Input.mousePosition;
            touchDownTime = Time.time;
            isTrackingTouch = true;
        }

        // 2. 手指抬起 (檢測是單點還是微彈)
        if (Input.GetMouseButtonUp(0) && isTrackingTouch)
        {
            isTrackingTouch = false;
            Vector2 touchUpPos = Input.mousePosition;
            Vector2 delta = touchUpPos - touchDownPos;
            float dragDist = delta.magnitude;
            float dragDuration = Time.time - touchDownTime;

            // 情況 A：若當前處於 PvP 命中的 Just-Frame 窗口內，且手指進行了「原地微彈」
            if (isInJustFrameWindow && currentTarget != null && currentTarget.IsHero)
            {
                if (dragDist >= minFlickPixels && dragDist <= maxFlickPixels && dragDuration < 0.25f)
                {
                    Vector3 worldFlickDir = ScreenDeltaToWorldDirection(delta);
                    ExecutePerfectMicroDash(worldFlickDir);
                    return;
                }
            }

            // 情況 B：常規單點 (點擊敵人或點擊地面)
            if (dragDist < minFlickPixels)
            {
                ProcessWorldTap(touchUpPos);
            }
        }
    }

    private void ProcessWorldTap(Vector2 screenPos)
    {
        Ray ray = mainCam.ScreenPointToRay(screenPos);

        // 優先檢測是否點擊敵人
        if (Physics.Raycast(ray, out RaycastHit hitTarget, 100f, targetLayer))
        {
            CombatTarget target = hitTarget.collider.GetComponent<CombatTarget>();
            if (target != null && target.IsAlive)
            {
                SetTarget(target);
                return;
            }
        }

        // 點擊地面：常規走位 (打斷後搖，若在打小兵則為普通走A)
        if (Physics.Raycast(ray, out RaycastHit hitGround, 100f, groundLayer))
        {
            IssueMoveCommand(hitGround.point);
        }
    }

    public void SetTarget(CombatTarget target)
    {
        currentTarget = target;
    }

    public void IssueMoveCommand(Vector3 destination)
    {
        currentTarget = null;
        isInJustFrameWindow = false;

        // 若正處於後搖階段，點地可立即取消後搖 (常態走A)
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    private void UpdateCombatTargeting()
    {
        if (currentTarget == null || !currentTarget.IsAlive)
        {
            currentTarget = null;
            return;
        }

        float distance = Vector3.Distance(transform.position, currentTarget.transform.position);

        // 射程外：朝目標接近
        if (distance > attackRange)
        {
            agent.isStopped = false;
            agent.SetDestination(currentTarget.transform.position);
            return;
        }

        // 進入射程：停步瞄準
        agent.isStopped = true;
        RotateTowards(currentTarget.transform.position);

        if (Time.time >= nextAttackTime && attackRoutine == null)
        {
            attackRoutine = StartCoroutine(ExecuteAttackCycle());
        }
    }

    private IEnumerator ExecuteAttackCycle()
    {
        // 1. 前搖階段 (Windup)
        yield return new WaitForSeconds(WindupDuration);

        // 2. 命中幀 (Hit Frame)
        PerformHit();

        // 開啟目押窗口 (僅在對戰英雄時有效解鎖微衝刺)
        if (currentTarget != null && currentTarget.IsHero)
        {
            isInJustFrameWindow = true;
            yield return new WaitForSeconds(justFrameWindow);
            isInJustFrameWindow = false;
        }

        // 3. 後搖階段 (Backswing - 若未被衝刺打斷則自然結束)
        yield return new WaitForSeconds(BackswingDuration);

        attackRoutine = null;
        nextAttackTime = Time.time + 0.05f;
    }

    private void PerformHit()
    {
        if (currentTarget == null || !currentTarget.IsAlive) return;

        currentTarget.TakeDamage(baseDamage, currentTarget.transform.position);
        nextAttackTime = Time.time + AttackPeriod;
    }

    /// <summary>
    /// 核心微操：執行 360 度完美微衝刺 (Micro-Dash)
    /// </summary>
    private void ExecutePerfectMicroDash(Vector3 direction)
    {
        isInJustFrameWindow = false;

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        // 攻速加速 20%
        nextAttackTime = Time.time + (AttackPeriod * 0.8f);

        // 執行瞬間位移
        agent.Move(direction.normalized * microDashDistance);

        // 觸發手機線性震動反饋
        #if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
        #endif

        OnPerfectCadenceDash?.Invoke();
        Debug.Log("<color=cyan>★ PERFECT MICRO-FLICK! 360度微衝刺觸發！</color>");
    }

    /// <summary>
    /// 防無腦狂戳 (Anti-Mashing) 邏輯
    /// </summary>
    private void RecordClickForAntiMashing()
    {
        float now = Time.time;
        if (now - lastClickTime < mashingThresholdTime)
        {
            recentClickCount++;
            if (recentClickCount > maxAllowedClicks && !isJammed)
            {
                TriggerCadenceJam();
            }
        }
        else
        {
            recentClickCount = 1;
        }
        lastClickTime = now;
    }

    private void TriggerCadenceJam()
    {
        isJammed = true;
        isInJustFrameWindow = false;
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        agent.isStopped = true;
        OnCadenceJam?.Invoke();
        Debug.LogWarning("<color=red>【卡刀硬直！】檢測到無腦狂戳，角色動作踉蹌 0.25 秒！</color>");

        StartCoroutine(RecoverFromJamRoutine());
    }

    private IEnumerator RecoverFromJamRoutine()
    {
        yield return new WaitForSeconds(jamStumbleDuration);
        isJammed = false;
        recentClickCount = 0;
    }

    private Vector3 ScreenDeltaToWorldDirection(Vector2 screenDelta)
    {
        Vector3 forward = mainCam.transform.forward;
        Vector3 right = mainCam.transform.right;
        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();

        Vector3 worldDir = right * screenDelta.x + forward * screenDelta.y;
        return worldDir.normalized;
    }

    private void RotateTowards(Vector3 targetPos)
    {
        Vector3 dir = (targetPos - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 18f);
        }
    }
}
