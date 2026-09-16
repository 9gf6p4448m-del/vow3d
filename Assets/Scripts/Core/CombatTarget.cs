using UnityEngine;

/// <summary>
/// 目標實體組件：用於區分 PvP 英雄與 PvE 小兵/野怪/建築
/// 《VOW 誓約》核心規則：只有攻擊 Hero 才能解鎖「微衝刺滑步」
/// </summary>
public class CombatTarget : MonoBehaviour
{
    public enum TargetType
    {
        Hero,       // 敵方英雄 (PvP：命中解鎖微衝刺)
        Minion,     // 小兵/野怪 (PvE：嚴格禁止微衝刺，僅允許普通取消後搖)
        Structure   // 晶塔/防禦建築
    }

    [Header("目標屬性")]
    [SerializeField] private TargetType targetType = TargetType.Minion;
    [SerializeField] private float maxHealth = 1000f;
    [SerializeField] private float currentHealth;

    public TargetType Type => targetType;
    public bool IsHero => targetType == TargetType.Hero;
    public bool IsAlive => currentHealth > 0f;

    private void Awake()
    {
        currentHealth = maxHealth;
        if (GetComponent<Collider>() == null)
        {
            gameObject.AddComponent<CapsuleCollider>();
        }
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (!IsAlive) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        Debug.Log($"[{name}] 受到 {amount} 傷害！剩餘生命: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"[{name}] 已陣亡！");
        gameObject.SetActive(false);
    }
}
