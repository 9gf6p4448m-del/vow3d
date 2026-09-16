using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》六邊形地脈板塊實體 (Hex Tile Plate)
/// 規則：
/// 1. 記錄 6 向鄰接節點圖論拓撲。
/// 2. 支援沉降 1 米並翻轉的地貌動畫。
/// 3. 過載反噬：若在過載狀態下被奪回，引發全域地震衝擊波並將相鄰 2 塊敵方板塊洗回中立。
/// </summary>
public class HexTilePlate : MonoBehaviour
{
    [Header("板塊屬性")]
    [SerializeField] private int plateId;
    [SerializeField] private TeamId currentOwner = TeamId.Neutral;
    [SerializeField] private bool isActiveFrontline = false;
    [SerializeField] private bool isOverloaded = false;

    [Header("組件引用")]
    [SerializeField] private LeylineSpire spire;
    [SerializeField] private List<HexTilePlate> neighbors = new List<HexTilePlate>();
    [SerializeField] private MeshRenderer plateRenderer;

    public int Id => plateId;
    public TeamId Owner => currentOwner;
    public bool IsActiveFrontline => isActiveFrontline;
    public bool IsOverloaded => isOverloaded;
    public List<HexTilePlate> Neighbors => neighbors;

    public event Action<HexTilePlate, TeamId> OnPlateOwnerChanged;

    private void Awake()
    {
        if (spire == null)
            spire = GetComponentInChildren<LeylineSpire>();

        if (spire != null)
            spire.OnSpireCaptured += HandleSpireCaptured;

        UpdatePlateVisualColor();
    }

    public void SetFrontline(bool isFrontline)
    {
        isActiveFrontline = isFrontline;
    }

    public void SetOverloadState(bool overload)
    {
        isOverloaded = overload;
        if (spire != null)
            spire.SetOverloaded(overload);

        UpdatePlateVisualColor();
    }

    private void HandleSpireCaptured(TeamId newOwner)
    {
        TeamId oldOwner = currentOwner;
        currentOwner = newOwner;

        // 若在「過載狀態」下被劣勢方奪回 ➔ 觸發【地脈大地震反噬】！
        if (isOverloaded && oldOwner != TeamId.Neutral)
        {
            TriggerOverloadRebellionShockwave(oldOwner);
        }

        StartCoroutine(SinkingFlipRoutine(newOwner));
        OnPlateOwnerChanged?.Invoke(this, newOwner);
    }

    /// <summary>
    /// 執行黑白棋/圍棋式的翻轉轉化
    /// </summary>
    public void ForceFlipToTeam(TeamId newOwner)
    {
        if (currentOwner == newOwner) return;

        currentOwner = newOwner;
        isOverloaded = false;
        if (spire != null)
            spire.TakeDamage(99999f, newOwner); // 強制摧毀同步

        StartCoroutine(SinkingFlipRoutine(newOwner));
        OnPlateOwnerChanged?.Invoke(this, newOwner);
    }

    /// <summary>
    /// 重置回中立灰色
    /// </summary>
    public void ResetToNeutral()
    {
        currentOwner = TeamId.Neutral;
        isOverloaded = false;
        UpdatePlateVisualColor();
        Debug.Log($"<color=grey>[地脈洗滌] 板塊 #{plateId} 被震回中立狀態！</color>");
    }

    /// <summary>
    /// 過載反噬大地震：眩暈敵軍，強制洗回 2 塊相鄰敵方板塊
    /// </summary>
    private void TriggerOverloadRebellionShockwave(TeamId enemyTeam)
    {
        Debug.Log("<color=red>★★★★【地脈反噬大地震爆發！】過載領土被奪回！★★★★</color>");

        // 眩暈半徑內敵軍
        Collider[] victims = Physics.OverlapSphere(transform.position, 12f);
        foreach (var victim in victims)
        {
            CombatTarget target = victim.GetComponent<CombatTarget>();
            if (target != null && target.IsHero)
            {
                target.TakeDamage(150f, victim.transform.position);
            }
        }

        // 洗回相鄰 2 塊敵方板塊
        int resetCount = 0;
        foreach (var neighbor in neighbors)
        {
            if (neighbor.Owner == enemyTeam)
            {
                neighbor.ResetToNeutral();
                resetCount++;
                if (resetCount >= 2) break;
            }
        }
    }

    private IEnumerator SinkingFlipRoutine(TeamId newOwner)
    {
        Vector3 initialPos = transform.position;
        Vector3 sunkenPos = initialPos + Vector3.down * 1.5f;

        // 1. 下沉 1.5 米
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(initialPos, sunkenPos, elapsed / 0.3f);
            yield return null;
        }

        // 2. 翻轉顏色
        UpdatePlateVisualColor();

        // 3. 升起歸位
        elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(sunkenPos, initialPos, elapsed / 0.3f);
            yield return null;
        }
        transform.position = initialPos;
    }

    private void UpdatePlateVisualColor()
    {
        if (plateRenderer == null) return;

        if (isOverloaded)
        {
            plateRenderer.material.color = Color.magenta; // 過載高溫紫色
            return;
        }

        plateRenderer.material.color = currentOwner switch
        {
            TeamId.Blue => new Color(0.1f, 0.5f, 1.0f),
            TeamId.Red => new Color(1.0f, 0.2f, 0.2f),
            _ => new Color(0.4f, 0.4f, 0.4f)
        };
    }
}
