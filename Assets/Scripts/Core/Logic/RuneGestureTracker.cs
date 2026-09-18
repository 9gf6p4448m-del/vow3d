using System;

namespace Vow.Core.Logic
{
    public enum RuneGestureOutcome
    {
        None,
        DragUpdated,  // 拖曳中：DirX／DirY／Distance01 已更新
        QuickCast,    // 輕點：從未拖出門檻就離手，或只是拇指在短促點擊中滾了一下
        Released,     // 拖曳後在取消區之外離手
        Cancelled     // 拖曳後滑回原點、滑進取消區離手，或觸控被系統中斷
    }

    // 地脈符印的單指手勢（GDD §參-1）。值型別、無配置。
    // 浮動原點：以手指按下的位置為圓心（與微輪盤一致，盲操不必對準按鈕中心）。
    // 方向是連續角度、不做八向吸附——石牆要能「100% 自由預判封路」。
    public struct RuneGestureTracker
    {
        public bool Held;
        public int TouchId;
        public float OriginX;
        public float OriginY;
        public double StartTime;
        public bool Dragging;      // 曾經拖出門檻
        public bool InsideOrigin;  // 拖曳後又滑回原點門檻內＝取消區
        public float MaxReach;     // 整段手勢離原點最遠的距離（像素）
        public float DirX;
        public float DirY;
        public float Distance01;

        public void Begin(int touchId, float x, float y, double time)
        {
            Held = true;
            TouchId = touchId;
            OriginX = x;
            OriginY = y;
            StartTime = time;
            Dragging = false;
            InsideOrigin = true;
            MaxReach = 0f;
            DirX = 0f;
            DirY = 0f;
            Distance01 = 0f;
        }

        public RuneGestureOutcome Move(float x, float y, float dragThresholdPx, float saturationPx)
        {
            if (!Held) return RuneGestureOutcome.None;

            float dx = x - OriginX;
            float dy = y - OriginY;
            double dist = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (dist > MaxReach) MaxReach = (float)dist;

            if (dist < dragThresholdPx)
            {
                // 滑回原點：方向與距離保留上一筆（虛影停在原地），只標記「此刻放手＝取消」
                InsideOrigin = true;
                return Dragging ? RuneGestureOutcome.DragUpdated : RuneGestureOutcome.None;
            }

            Dragging = true;
            InsideOrigin = false;
            DirX = (float)(dx / dist);
            DirY = (float)(dy / dist);

            float span = saturationPx - dragThresholdPx;
            float t = span > 0f ? (float)((dist - dragThresholdPx) / span) : 1f;
            Distance01 = t < 0f ? 0f : (t > 1f ? 1f : t);
            return RuneGestureOutcome.DragUpdated;
        }

        public RuneGestureOutcome End(float x, float y, double time, float dragThresholdPx, float saturationPx,
            float tapSlopPx, double tapMaxSeconds, bool inCancelZone)
        {
            if (!Held) return RuneGestureOutcome.None;

            // 最後一個取樣點也要算（快速拖曳可能只有 Began→Ended 兩個取樣）
            Move(x, y, dragThresholdPx, saturationPx);
            Held = false;

            if (!Dragging) return RuneGestureOutcome.QuickCast;

            // 短促點擊時拇指會在螢幕上滾動，位移越過 3.5mm 的拖曳門檻再滾回來。若把它當成「滑回原點＝取消」，
            // 玩家只會覺得按了沒反應（吃指令）。夠快、滾得夠小、放手時已回到原點，就是輕點。
            // 放手時仍在門檻外的不赦免：那是高手的快速方向施放（往左甩 5mm 就要在左邊立牆），吞成「正前方」等於把方向丟掉。
            if (InsideOrigin && time - StartTime <= tapMaxSeconds && MaxReach <= tapSlopPx) return RuneGestureOutcome.QuickCast;

            if (InsideOrigin || inCancelZone) return RuneGestureOutcome.Cancelled;
            return RuneGestureOutcome.Released;
        }

        // 觸控被系統中斷、切換操作模式：已經亮出虛影的要通知收掉；還沒拖曳的靜靜結束（不得變成極速石牆）。
        public RuneGestureOutcome Cancel()
        {
            if (!Held) return RuneGestureOutcome.None;
            Held = false;
            return Dragging ? RuneGestureOutcome.Cancelled : RuneGestureOutcome.None;
        }
    }
}
