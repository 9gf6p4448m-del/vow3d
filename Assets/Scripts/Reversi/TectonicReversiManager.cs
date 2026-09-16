using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》地脈圍棋核心管理器 (Tectonic Reversi Manager)
/// 規則：
/// 1. 管理 24 塊六邊形板塊圖論拓撲。
/// 2. 閉環夾擊翻轉算法 (BFS / Flood Fill)。
/// 3. P3 防雪球：領土 >= 60% 觸發地脈過載；領土 >= 70% 觸發終局勝利。
/// 4. 維護三點動態交戰前線 (Active Frontlines)。
/// </summary>
public class TectonicReversiManager : MonoBehaviour
{
    public static TectonicReversiManager Instance { get; private set; }

    [Header("全圖板塊數據 (共 24 塊)")]
    [SerializeField] private List<HexTilePlate> allPlates = new List<HexTilePlate>();

    [Header("對局時間控制 (15分鐘黃金對局)")]
    [SerializeField] private float matchDuration = 900f; // 15 分鐘
    [SerializeField] private float timeElapsed = 0f;

    public const float OVERLOAD_THRESHOLD_RATIO = 0.60f; // 60% (15塊) 觸發過載
    public const float VICTORY_THRESHOLD_RATIO = 0.70f;  // 70% (17塊) 終局勝利

    public event Action<TeamId> OnMatchVictory;

    private bool isMatchEnded = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        foreach (var plate in allPlates)
        {
            plate.OnPlateOwnerChanged += HandlePlateStateChange;
        }
        UpdateDynamicFrontlines();
    }

    private void Update()
    {
        if (isMatchEnded) return;

        timeElapsed += Time.deltaTime;
        if (timeElapsed >= matchDuration)
        {
            EndMatchByTimeLimit();
        }
    }

    /// <summary>
    /// 當任一板塊所有權改變時觸發：
    /// 1. 執行圍棋閉環夾擊翻轉檢測
    /// 2. 重新結算過載與勝負
    /// 3. 更新動態前線
    /// </summary>
    private void HandlePlateStateChange(HexTilePlate changedPlate, TeamId newOwner)
    {
        if (isMatchEnded || newOwner == TeamId.Neutral) return;

        // 1. 執行包圍夾擊翻轉
        CheckAndExecuteEncirclementFlips(newOwner);

        // 2. 領土統計與勝負結算
        EvaluateTerritoryBalance();

        // 3. 更新動態前線
        UpdateDynamicFrontlines();
    }

    /// <summary>
    /// 圖論演算法：檢測被 newOwner 隊伍夾擊包圍的敵方孤立板塊
    /// </summary>
    private void CheckAndExecuteEncirclementFlips(TeamId capturingTeam)
    {
        TeamId enemyTeam = (capturingTeam == TeamId.Blue) ? TeamId.Red : TeamId.Blue;
        List<HexTilePlate> platesToFlip = new List<HexTilePlate>();

        foreach (var plate in allPlates)
        {
            if (plate.Owner == enemyTeam)
            {
                // 檢查是否被完全孤立包圍 (所有鄰居皆非敵方或皆為 capturingTeam)
                bool isEncircled = true;
                foreach (var neighbor in plate.Neighbors)
                {
                    if (neighbor.Owner == enemyTeam)
                    {
                        isEncircled = false;
                        break;
                    }
                }

                if (isEncircled)
                {
                    platesToFlip.Add(plate);
                }
            }
        }

        // 翻轉所有被包圍板塊
        foreach (var plate in platesToFlip)
        {
            Debug.Log($"<color=cyan>★【圍棋夾擊斷脈！】板塊 #{plate.Id} 被包圍，翻轉歸屬: {capturingTeam}</color>");
            plate.ForceFlipToTeam(capturingTeam);
        }
    }

    private void EvaluateTerritoryBalance()
    {
        int blueCount = 0;
        int redCount = 0;
        int total = allPlates.Count;

        foreach (var plate in allPlates)
        {
            if (plate.Owner == TeamId.Blue) blueCount++;
            else if (plate.Owner == TeamId.Red) redCount++;
        }

        float blueRatio = (float)blueCount / total;
        float redRatio = (float)redCount / total;

        // 1. 檢查是否達到 70% 終局勝利
        if (blueRatio >= VICTORY_THRESHOLD_RATIO)
        {
            TriggerVictory(TeamId.Blue);
            return;
        }
        if (redRatio >= VICTORY_THRESHOLD_RATIO)
        {
            TriggerVictory(TeamId.Red);
            return;
        }

        // 2. 檢查是否觸發 60% 地脈過載反噬
        ApplyOverloadState(TeamId.Blue, blueRatio >= OVERLOAD_THRESHOLD_RATIO);
        ApplyOverloadState(TeamId.Red, redRatio >= OVERLOAD_THRESHOLD_RATIO);
    }

    private void ApplyOverloadState(TeamId team, bool isOverloaded)
    {
        foreach (var plate in allPlates)
        {
            if (plate.Owner == team)
            {
                // 將位於前線/邊緣的板塊標記為過載高溫
                plate.SetOverloadState(isOverloaded && plate.IsActiveFrontline);
            }
        }
    }

    /// <summary>
    /// 更新三點動態前線 (Active Frontlines)：只允許交界處板塊被攻擊爭奪
    /// </summary>
    private void UpdateDynamicFrontlines()
    {
        foreach (var plate in allPlates)
        {
            bool isBorder = false;
            foreach (var neighbor in plate.Neighbors)
            {
                if (neighbor.Owner != plate.Owner)
                {
                    isBorder = true;
                    break;
                }
            }
            plate.SetFrontline(isBorder);
        }
    }

    private void TriggerVictory(TeamId winner)
    {
        isMatchEnded = true;
        Debug.Log($"<color=green>★★★★★★【終局大勝！】{winner} 隊伍成功掌控 70% 地脈領土，敵方要塞大坍塌獲勝！★★★★★★</color>");
        OnMatchVictory?.Invoke(winner);
    }

    private void EndMatchByTimeLimit()
    {
        isMatchEnded = true;
        int blue = 0, red = 0;
        foreach (var p in allPlates)
        {
            if (p.Owner == TeamId.Blue) blue++;
            else if (p.Owner == TeamId.Red) red++;
        }

        TeamId winner = (blue > red) ? TeamId.Blue : (red > blue) ? TeamId.Red : TeamId.Neutral;
        Debug.Log($"<color=yellow>【15分鐘倒計時結束】藍方 {blue} 塊 vs 紅方 {red} 塊 ➔ 獲勝者: {winner}</color>");
        OnMatchVictory?.Invoke(winner);
    }
}
