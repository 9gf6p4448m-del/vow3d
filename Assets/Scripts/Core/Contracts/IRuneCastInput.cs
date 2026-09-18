using System;
using UnityEngine;

namespace Vow.Core
{
    // ARCHITECTURE.md 的 IPlayerInputService 只定義了符印的「拖曳更新／極速施放／取消」，沒有「鬆手成牆」。
    // 那份契約逐字保留不動，缺的這一個事件補在這裡；輸入服務同時實作兩個介面。
    public interface IRuneCastInput
    {
        // 拖曳後在取消區之外鬆手：螢幕方向（單位向量）、拉伸量 0~1。
        event Action<Vector2, float> OnRuneCastReleased;
    }
}
