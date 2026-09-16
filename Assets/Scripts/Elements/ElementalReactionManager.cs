using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》四大元素反應管理器 (Elemental Reaction Engine)
/// 規則：
/// 1. 統一 1.5 倍化學反應傷害倍率。
/// 2. 觸發蒸氣迷霧、泥濘流沙、烈焰風暴與狂風巨浪。
/// </summary>
public class ElementalReactionManager : MonoBehaviour
{
    public static ElementalReactionManager Instance { get; private set; }

    [Header("反應生成物預製體")]
    [SerializeField] private GameObject reactionFieldPrefab;

    public const float REACTION_DAMAGE_MULTIPLIER = 1.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 核心結算：向目標施加元素攻擊，檢測是否觸發反應
    /// </summary>
    public float ApplyElementalAttack(CombatTarget target, Element incomingElement, float baseDamage, Vector3 hitPosition)
    {
        if (target == null || !target.IsAlive) return 0f;

        // 檢查目標身上的現存元素附著組件
        ElementalAuraReceiver receiver = target.GetComponent<ElementalAuraReceiver>();
        if (receiver == null)
        {
            receiver = target.gameObject.AddComponent<ElementalAuraReceiver>();
        }

        Element currentElement = receiver.CurrentElement;
        ElementalReaction reaction = EvaluateReaction(currentElement, incomingElement);

        float finalDamage = baseDamage;

        if (reaction != ElementalReaction.None)
        {
            // 觸發化學反應：享有 1.5 倍統一傷害增幅
            finalDamage *= REACTION_DAMAGE_MULTIPLIER;
            receiver.ClearAura(); // 反應消耗附著元素

            TriggerReactionField(reaction, hitPosition);
            Debug.Log($"<color=yellow>★【元素反應: {reaction}】觸發！造成 1.5 倍暴擊傷害: {finalDamage}</color>");
        }
        else
        {
            // 未觸發反應：附著新元素 (持續 6 秒)
            receiver.ApplyAura(incomingElement, 6.0f);
        }

        target.TakeDamage(finalDamage, hitPosition);
        return finalDamage;
    }

    private ElementalReaction EvaluateReaction(Element existing, Element incoming)
    {
        if (existing == Element.None || incoming == Element.None || existing == incoming)
            return ElementalReaction.None;

        // 水 + 火 ➔ 蒸氣
        if ((existing == Element.Water && incoming == Element.Fire) || (existing == Element.Fire && incoming == Element.Water))
            return ElementalReaction.SteamFog;

        // 水 + 岩 ➔ 泥濘流沙
        if ((existing == Element.Water && incoming == Element.Earth) || (existing == Element.Earth && incoming == Element.Water))
            return ElementalReaction.MudQuicksand;

        // 風 + 火 ➔ 烈焰風暴
        if ((existing == Element.Wind && incoming == Element.Fire) || (existing == Element.Fire && incoming == Element.Wind))
            return ElementalReaction.Firestorm;

        // 風 + 水 ➔ 狂風巨浪
        if ((existing == Element.Wind && incoming == Element.Water) || (existing == Element.Water && incoming == Element.Wind))
            return ElementalReaction.GaleWave;

        return ElementalReaction.None;
    }

    private void TriggerReactionField(ElementalReaction reaction, Vector3 spawnPos)
    {
        if (reactionFieldPrefab == null)
        {
            // 臨時動態構建場域實體
            GameObject fieldObj = new GameObject($"Field_{reaction}");
            ReactionFieldEffect effect = fieldObj.AddComponent<ReactionFieldEffect>();
            effect.Initialize(reaction, spawnPos);
            return;
        }

        GameObject field = Instantiate(reactionFieldPrefab, spawnPos, Quaternion.identity);
        ReactionFieldEffect fieldEffect = field.GetComponent<ReactionFieldEffect>();
        if (fieldEffect != null)
        {
            fieldEffect.Initialize(reaction, spawnPos);
        }
    }
}

/// <summary>
/// 掛載於目標身上的元素附著器
/// </summary>
public class ElementalAuraReceiver : MonoBehaviour
{
    private Element currentElement = Element.None;
    private float expiryTime = 0f;

    public Element CurrentElement => (Time.time < expiryTime) ? currentElement : Element.None;

    public void ApplyAura(Element element, float duration)
    {
        currentElement = element;
        expiryTime = Time.time + duration;
        Debug.Log($"[{name}] 附著元素: {element} (持續 {duration}s)");
    }

    public void ClearAura()
    {
        currentElement = Element.None;
        expiryTime = 0f;
    }
}
