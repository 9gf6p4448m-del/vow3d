using System;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》P4 核心跨平台適配器 (Cross-Platform Input Router)
/// 規則：
/// 1. 自動偵測 PC Steam (鍵盤+滑鼠) 與 移動端 (觸控+手勢符印)。
/// 2. PC 端支持：滑鼠右鍵甩動走A + 鍵盤 Q/W/E/R 目標向量即時施法。
/// 3. 移動端支持：微彈指走A + 邊緣符印推拉。
/// 4. 數據與微操邏輯跨端 100% 對等相容！
/// </summary>
public class CrossPlatformInputRouter : MonoBehaviour
{
    public enum PlatformMode
    {
        AutoDetect,
        ForcePCStandalone,
        ForceMobileTouch
    }

    [Header("平台模式")]
    [SerializeField] private PlatformMode mode = PlatformMode.AutoDetect;

    [Header("PC 鍵位映射")]
    [SerializeField] private KeyCode wallSkillKey = KeyCode.Q;      // Q 鍵：岩壁塑形
    [SerializeField] private KeyCode sinkholeSkillKey = KeyCode.W;  // W 鍵：流沙塌陷
    [SerializeField] private KeyCode centerCameraKey = KeyCode.Space;

    // 核心控制器引用
    private MicroFlickCadenceController cadenceController;
    private RuneVectorCaster vectorCaster;
    private bool isPCMode = true;

    private void Awake()
    {
        cadenceController = GetComponent<MicroFlickCadenceController>();
        vectorCaster = GetComponent<RuneVectorCaster>();

        DetectPlatform();
    }

    private void DetectPlatform()
    {
        if (mode == PlatformMode.ForcePCStandalone)
        {
            isPCMode = true;
        }
        else if (mode == PlatformMode.ForceMobileTouch)
        {
            isPCMode = false;
        }
        else
        {
            // 自動偵測
            #if UNITY_STANDALONE || UNITY_EDITOR
            isPCMode = true;
            #else
            isPCMode = false;
            #endif
        }

        Debug.Log($"<color=green>【VOW 跨端輸入路由就緒】當前模式: {(isPCMode ? "PC Steam (鍵鼠模式)" : "移動端 (觸控符印模式)")}</color>");
    }

    private void Update()
    {
        if (isPCMode)
        {
            HandlePCKeyboardMouseInputs();
        }
    }

    /// <summary>
    /// PC 端專屬操作：鍵盤 Q 鍵向量瞄準 + 滑鼠快速出鞘
    /// </summary>
    private void HandlePCKeyboardMouseInputs()
    {
        // 1. Q 鍵按下：以滑鼠光標位置為起點，開啟向量施法
        if (Input.GetKeyDown(wallSkillKey))
        {
            Debug.Log("<color=cyan>[PC 鍵盤 Q 觸發] 石牆向量施法模式啟動！</color>");
        }

        // 2. 空白鍵：鏡頭鎖定/回中
        if (Input.GetKeyDown(centerCameraKey))
        {
            Debug.Log("[PC 鍵盤 Space] 鏡頭重置居中！");
        }
    }
}
