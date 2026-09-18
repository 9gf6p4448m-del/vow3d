using System;
using UnityEngine;

namespace Vow.Core
{
    // ARCHITECTURE.md §參-1
    public interface IPlayerInputService
    {
        ControlMode ActiveMode { get; set; }

        // 點擊移動與目標選擇事件
        event Action<Vector3> OnMoveDestinationSelected;
        event Action<ICombatTarget> OnCombatTargetSelected;

        // 微滑步身位向量輸入 (已考慮物理毫米 PPI 換算與邊緣防誤觸)；螢幕座標系、已正規化並吸附八向
        event Action<Vector2> OnCadenceVectorFlicked;

        // 符印石牆長按/雙擊事件 (強制阻斷向普攻控制器滲透)；Phase 1 僅保留契約，不發事件
        event Action<Vector2, float> OnRuneVectorDragUpdated; // 旋轉角度與距離
        event Action OnRuneQuickCastTriggered;
        event Action OnRuneCastCancelled;
    }
}
