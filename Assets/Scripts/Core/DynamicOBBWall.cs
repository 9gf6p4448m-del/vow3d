using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 《VOW 誓約》P2 核心障礙物：確定性幾何石牆 (Dynamic OBB Wall)
/// 規則：
/// 1. 具有確定性 OBB 幾何尺寸，伺服器與客戶端完全對齊。
/// 2. 5 秒倒計時自癒下沉銷毀。
/// 3. 支援近戰擊碎 (Shatter)，碎石產生物理反傷。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DynamicOBBWall : MonoBehaviour
{
    [Header("石牆幾何尺寸")]
    [SerializeField] private Vector3 wallSize = new Vector3(3.0f, 2.5f, 0.8f);
    [SerializeField] private float lifetime = 5.0f;
    [SerializeField] private float riseSpeed = 10.0f;

    [Header("近戰擊碎反傷")]
    [SerializeField] private float shatterDamage = 120f;
    [SerializeField] private float shatterRadius = 3.5f;
    [SerializeField] private LayerMask enemyLayer;

    private BoxCollider boxCollider;
    private NavMeshObstacle obstacle;
    private Vector3 targetPos;
    private Vector3 hiddenPos;

    private void Awake()
    {
        boxCollider = GetComponent<BoxCollider>();
        boxCollider.size = wallSize;

        obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.size = wallSize;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
        }
    }

    public void Initialize(Vector3 spawnPosition, Quaternion rotation)
    {
        targetPos = spawnPosition;
        hiddenPos = spawnPosition + Vector3.down * wallSize.y;

        transform.position = hiddenPos;
        transform.rotation = rotation;

        StartCoroutine(RiseAndDecayRoutine());
    }

    private IEnumerator RiseAndDecayRoutine()
    {
        // 1. 破土而出的升起動畫
        float elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime * riseSpeed;
            transform.position = Vector3.Lerp(hiddenPos, targetPos, elapsed);
            yield return null;
        }
        transform.position = targetPos;

        // 2. 戰術阻擋窗口
        yield return new WaitForSeconds(lifetime);

        // 3. 地脈自我修復 (沉降銷毀)
        elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime * (riseSpeed * 0.5f);
            transform.position = Vector3.Lerp(targetPos, hiddenPos, elapsed);
            yield return null;
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 被近戰重擊擊碎時調用：引發碎石飛濺反傷
    /// </summary>
    public void ShatterByMelee(Vector3 impactDirection)
    {
        StopAllCoroutines();

        // 碎石飛散傷害檢測
        Collider[] victims = Physics.OverlapSphere(transform.position + impactDirection.normalized * 2f, shatterRadius, enemyLayer);
        foreach (var victim in victims)
        {
            CombatTarget target = victim.GetComponent<CombatTarget>();
            if (target != null)
            {
                target.TakeDamage(shatterDamage, victim.transform.position);
                Debug.Log($"<color=orange>【碎石反擊！】石牆爆裂擊中: {victim.name}</color>");
            }
        }

        Destroy(gameObject);
    }
}
