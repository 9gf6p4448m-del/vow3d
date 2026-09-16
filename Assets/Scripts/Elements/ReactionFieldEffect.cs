using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》元素反應地表實體：蒸氣迷霧、泥濘流沙、烈焰風暴
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class ReactionFieldEffect : MonoBehaviour
{
    [Header("場域基礎屬性")]
    [SerializeField] private ElementalReaction reactionType = ElementalReaction.None;
    [SerializeField] private float duration = 5.0f;
    [SerializeField] private float radius = 4.0f;

    [Header("數值效果")]
    [SerializeField] private float damagePerSecond = 45f;
    [SerializeField] private float slowMultiplier = 0.4f; // 剩餘 40% 移速 (減速 60%)

    private SphereCollider sphereCol;
    private List<CombatTarget> activeTargets = new List<CombatTarget>();

    private void Awake()
    {
        sphereCol = GetComponent<SphereCollider>();
        sphereCol.isTrigger = true;
        sphereCol.radius = radius;
    }

    public void Initialize(ElementalReaction type, Vector3 position, float customDuration = 5.0f)
    {
        reactionType = type;
        duration = customDuration;
        transform.position = position;

        StartCoroutine(FieldLifetimeRoutine());
        StartCoroutine(PeriodicEffectRoutine());
    }

    private IEnumerator FieldLifetimeRoutine()
    {
        yield return new WaitForSeconds(duration);
        Destroy(gameObject);
    }

    private IEnumerator PeriodicEffectRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            // 清理已失效或死亡的目標
            activeTargets.RemoveAll(t => t == null || !t.IsAlive);

            foreach (var target in activeTargets)
            {
                ApplyFieldTick(target);
            }
        }
    }

    private void ApplyFieldTick(CombatTarget target)
    {
        switch (reactionType)
        {
            case ElementalReaction.Firestorm:
                // 烈焰風暴：持續造成 1.5x 爆燃傷害
                target.TakeDamage(damagePerSecond * 0.5f * 1.5f, target.transform.position);
                break;

            case ElementalReaction.MudQuicksand:
                // 泥濘流沙：定時微量傷害與減速維護
                target.TakeDamage(10f, target.transform.position);
                break;

            case ElementalReaction.SteamFog:
                // 蒸氣迷霧：不造成傷害，阻隔遠程鎖定視野 (Fog of War)
                break;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CombatTarget target = other.GetComponent<CombatTarget>();
        if (target != null && !activeTargets.Contains(target))
        {
            activeTargets.Add(target);
            OnTargetEnterField(target);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        CombatTarget target = other.GetComponent<CombatTarget>();
        if (target != null && activeTargets.Contains(target))
        {
            activeTargets.Remove(target);
            OnTargetExitField(target);
        }
    }

    private void OnTargetEnterField(CombatTarget target)
    {
        if (reactionType == ElementalReaction.MudQuicksand)
        {
            Debug.Log($"<color=brown>[流沙陷入] {target.name} 移速降低 60%，無法衝刺！</color>");
        }
        else if (reactionType == ElementalReaction.SteamFog)
        {
            Debug.Log($"<color=white>[進入蒸氣] {target.name} 進入視野盲區！</color>");
        }
    }

    private void OnTargetExitField(CombatTarget target)
    {
        if (reactionType == ElementalReaction.MudQuicksand)
        {
            Debug.Log($"<color=green>[脫離流沙] {target.name} 恢復正常移速。</color>");
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = reactionType switch
        {
            ElementalReaction.SteamFog => Color.white,
            ElementalReaction.MudQuicksand => new Color(0.6f, 0.4f, 0.1f, 0.4f),
            ElementalReaction.Firestorm => Color.red,
            _ => Color.yellow
        };
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
