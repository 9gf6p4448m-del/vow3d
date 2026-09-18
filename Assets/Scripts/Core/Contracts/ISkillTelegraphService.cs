using UnityEngine;

namespace Vow.Core
{
    public enum TelegraphShape
    {
        LineCast,   // 線性技能投射箭頭 (如長矛穿刺、箭矢彈道)
        ZoneCast    // 範圍技能法陣 (如冰霜牢獄、落雷轟炸)
    }

    // ARCHITECTURE.md §陸-1
    public interface ISkillTelegraphService
    {
        TelegraphShape ActiveShape { get; }
        bool IsAiming { get; }

        // 開啟預警指示器：設定基礎長度、寬度或半徑
        void ShowLineIndicator(Vector3 origin, Vector3 direction, float length, float width);
        void ShowZoneIndicator(Vector3 center, float radius, float edgeThickness);

        // 更新瞄準位置 (滑鼠牽引或左/右微輪盤瞄準)
        void UpdateAimTransform(Vector3 currentAimPosition);

        // 隱藏與釋放指示器 (0 GC，回收至對象池)
        void HideIndicator();

        // 指示器邊界動畫：外衝吸附 (Snap Animation) 與邊界高光
        void TriggerSnapFeedback();
    }
}
