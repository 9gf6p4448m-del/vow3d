using System;
using UnityEngine;

public enum TeamId
{
    Neutral = 0,
    Blue = 1,
    Red = 2
}

/// <summary>
/// 《VOW 誓約》地脈共振晶塔 (Leyline Spire)
/// 規則：
/// 1. 位於六邊形板塊中央，嚴禁小兵無腦佔領，必須由英雄實體攻擊破壞或灌注。
/// 2. 板塊處於「過載狀態」時，晶塔血量與防禦降低 40%。
/// </summary>
public class LeylineSpire : MonoBehaviour
{
    [Header("晶塔屬性")]
    [SerializeField] private TeamId currentOwner = TeamId.Neutral;
    [SerializeField] private float baseMaxHealth = 1500f;
    [SerializeField] private float currentHealth;
    [SerializeField] private bool isOverloaded = false;

    public TeamId Owner => currentOwner;
    public bool IsOverloaded => isOverloaded;
    public float CurrentHealth => currentHealth;

    public event Action<TeamId> OnSpireCaptured;

    private HexTilePlate parentPlate;

    private void Awake()
    {
        currentHealth = baseMaxHealth;
        parentPlate = GetComponentInParent<HexTilePlate>();
    }

    public void SetOverloaded(bool overloaded)
    {
        isOverloaded = overloaded;
        if (isOverloaded)
        {
            // 過載狀態：血量上限削弱 40%
            currentHealth = Mathf.Min(currentHealth, baseMaxHealth * 0.6f);
            Debug.Log($"<color=red>[晶塔警報] {name} 進入過載狀態！防禦降低 40%！</color>");
        }
    }

    public void TakeDamage(float damage, TeamId attackerTeam)
    {
        if (attackerTeam == currentOwner || attackerTeam == TeamId.Neutral) return;

        // 過載時承受 1.4 倍傷害
        float finalDamage = isOverloaded ? damage * 1.4f : damage;
        currentHealth -= finalDamage;

        Debug.Log($"[{name}] 晶塔受擊！剩餘血量: {currentHealth:F0}");

        if (currentHealth <= 0f)
        {
            Capture(attackerTeam);
        }
    }

    private void Capture(TeamId newOwner)
    {
        currentOwner = newOwner;
        currentHealth = baseMaxHealth; // 佔領後重置血量
        isOverloaded = false;

        Debug.Log($"<color=cyan>★★★【晶塔易手！】{name} 已被 {newOwner} 方佔領！★★★</color>");
        OnSpireCaptured?.Invoke(newOwner);
    }
}
