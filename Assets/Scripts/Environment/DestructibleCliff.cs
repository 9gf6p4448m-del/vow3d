using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 《VOW 誓約》三層立體地勢：可破壞懸崖邊緣 (Destructible Cliff)
/// 規則：
/// 1. 低層英雄直接攻擊懸崖基座，打碎懸崖。
/// 2. 懸崖坍塌時，站在上方的遠程射手直接墜落中層，承受 1.0 秒摔倒硬直。
/// 3. 崩落巨石對下方區域造成物理碾壓傷害。
/// </summary>
public class DestructibleCliff : MonoBehaviour
{
    [Header("懸崖耐久度")]
    [SerializeField] private float cliffHealth = 400f;
    [SerializeField] private Vector3 highGroundZoneSize = new Vector3(4f, 2f, 3f);
    [SerializeField] private Vector3 highGroundZoneOffset = new Vector3(0f, 3f, 0f);

    [Header("坍塌數值")]
    [SerializeField] private float fallDamage = 150f;
    [SerializeField] private float fallStunDuration = 1.0f;
    [SerializeField] private float rubbleDamage = 180f;
    [SerializeField] private float rubbleRadius = 4f;
    [SerializeField] private LayerMask unitLayer;

    private bool isCollapsed = false;

    public void DamageCliffBase(float damage)
    {
        if (isCollapsed) return;

        cliffHealth -= damage;
        Debug.Log($"<color=yellow>[懸崖受擊] 剩餘耐久: {cliffHealth}</color>");

        if (cliffHealth <= 0f)
        {
            CollapseCliff();
        }
    }

    private void CollapseCliff()
    {
        isCollapsed = true;
        Debug.Log("<color=red>★★★【懸崖崩塌！】天脊邊緣破碎墜落！★★★</color>");

        // 1. 檢測站在懸崖上方的單位 (高地射手)
        Vector3 checkCenter = transform.position + highGroundZoneOffset;
        Collider[] victimsOnTop = Physics.OverlapBox(checkCenter, highGroundZoneSize * 0.5f, transform.rotation, unitLayer);

        foreach (var victim in victimsOnTop)
        {
            DropUnitFromCliff(victim.gameObject);
        }

        // 2. 墜落巨石對懸崖正下方區域造成物理碾壓
        Collider[] victimsBelow = Physics.OverlapSphere(transform.position, rubbleRadius, unitLayer);
        foreach (var victim in victimsBelow)
        {
            CombatTarget target = victim.GetComponent<CombatTarget>();
            if (target != null)
            {
                target.TakeDamage(rubbleDamage, victim.transform.position);
            }
        }

        // 3. 隱藏懸崖本體，生成碎石殘骸
        StartCoroutine(CollapseVisualRoutine());
    }

    private void DropUnitFromCliff(GameObject unit)
    {
        NavMeshAgent agent = unit.GetComponent<NavMeshAgent>();
        CombatTarget target = unit.GetComponent<CombatTarget>();

        if (target != null)
        {
            target.TakeDamage(fallDamage, unit.transform.position);
        }

        // 擊落至低處並施加 1 秒摔倒硬直
        Vector3 groundPos = transform.position + Vector3.forward * 2f;
        groundPos.y = transform.position.y; // 降至底層高度

        if (agent != null)
        {
            agent.Warp(groundPos);
            StartCoroutine(ApplyFallStun(agent));
        }

        Debug.Log($"<color=orange>【墜崖擊落！】{unit.name} 失足摔落高台，陷入 1 秒倒地硬直！</color>");
    }

    private IEnumerator ApplyFallStun(NavMeshAgent agent)
    {
        agent.isStopped = true;
        yield return new WaitForSeconds(fallStunDuration);
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = false;
        }
    }

    private IEnumerator CollapseVisualRoutine()
    {
        float elapsed = 0f;
        Vector3 initialScale = transform.localScale;
        while (elapsed < 0.5f)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, elapsed / 0.5f);
            yield return null;
        }
        gameObject.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position + highGroundZoneOffset, highGroundZoneSize);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, rubbleRadius);
    }
}
