using System;
using UnityEngine;

/// <summary>
/// 《VOW 誓約》四大基石元素與化學反應定義
/// </summary>
public enum Element
{
    None = 0,
    Earth = 1,  // 岩 (固體/護甲/石壁)
    Water = 2,  // 水 (液體/減速/導電)
    Wind = 3,   // 風 (動能/位移/擴散)
    Fire = 4    // 火 (熱能/燃燒/熔化)
}

public enum ElementalReaction
{
    None = 0,
    SteamFog,       // 水 + 火 ➔ 蒸氣迷霧 (遮蔽視野/致盲)
    MudQuicksand,   // 水 + 岩 ➔ 泥濘流沙 (大幅減速 60% / 禁空接地)
    Firestorm,      // 風 + 火 ➔ 烈焰風暴 (大範圍爆燃推進)
    GaleWave        // 風 + 水 ➔ 狂風巨浪 (強力擊退/洗刷地形)
}

/// <summary>
/// 元素附著狀態數據
/// </summary>
[System.Serializable]
public struct ElementAura
{
    public Element element;
    public float remainingDuration;
    public float potency; // 元素強度

    public ElementAura(Element elem, float duration, float power = 1.0f)
    {
        element = elem;
        remainingDuration = duration;
        potency = power;
    }

    public bool IsActive => element != Element.None && remainingDuration > 0f;
}
