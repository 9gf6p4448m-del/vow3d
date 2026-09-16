using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》第 10 分鐘深淵地核巨獸 (The Apex Colossus)
/// 規則：
/// 1. 比賽第 10 分鐘在中央深淵甦醒。
/// 2. 擊殺者隊伍直接「強制翻轉中央 3 塊核心板塊」。
/// 3. 全隊獲得 90 秒「地脈衝能」超高額破障增益，提供絕地翻盤大槓桿！
/// </summary>
public class ApexColossus : MonoBehaviour
{
    [Header("巨獸屬性")]
    [SerializeField] private float maxHealth = 6000f;
    [SerializeField] private float currentHealth;
    [SerializeField] private float spawnTime = 600f; // 10 分鐘
    [SerializeField] private List<HexTilePlate> centralCorePlates = new List<HexTilePlate>();

    [Header("戰鬥範圍")]
    [SerializeField] private float slamRadius = 6.0f;
    [SerializeField] private float slamDamage = 260f;

    private bool isSpawned = false;
    private bool isDefeated = false;

    private void Start()
    {
        currentHealth = maxHealth;
        gameObject.SetActive(false); // 初始隱藏
        StartCoroutine(SpawnTimerRoutine());
    }

    private IEnumerator SpawnTimerRoutine()
    {
        yield return new WaitForSeconds(spawnTime);
        SpawnColossus();
    }

    public void SpawnColossus()
    {
        isSpawned = true;
        gameObject.SetActive(true);
        Debug.Log("<color=red>★★★★★★【地核深淵巨獸甦醒！】遠古地脈之怒降臨戰場中央！★★★★★★</color>");
    }

    public void TakeDamage(float damage, TeamId attackerTeam)
    {
        if (!isSpawned || isDefeated) return;

        currentHealth -= damage;
        Debug.Log($"<color=red>[巨獸受創] 剩餘生命: {currentHealth:F0}/{maxHealth}</color>");

        if (currentHealth <= 0f)
        {
            DefeatColossus(attackerTeam);
        }
    }

    private void DefeatColossus(TeamId killerTeam)
    {
        isDefeated = true;
        Debug.Log($"<color=cyan>★★★★★★【巨獸被擊殺！】{killerTeam} 隊伍斬獲遠古神力！★★★★★★</color>");

        // 1. 強制翻轉中央 3 塊核心板塊
        foreach (var plate in centralCorePlates)
        {
            if (plate != null)
            {
                plate.ForceFlipToTeam(killerTeam);
            }
        }

        // 2. 觸發全圖震波與震退
        Collider[] victims = Physics.OverlapSphere(transform.position, slamRadius * 2f);
        foreach (var victim in victims)
        {
            CombatTarget target = victim.GetComponent<CombatTarget>();
            if (target != null && target.IsHero)
            {
                target.TakeDamage(slamDamage, victim.transform.position);
            }
        }

        gameObject.SetActive(false);
    }
}
