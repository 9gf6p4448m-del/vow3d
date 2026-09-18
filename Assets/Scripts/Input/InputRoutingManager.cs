using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Input
{
    public enum TouchRoute
    {
        Rejected,     // 落在邊緣防誤觸死區
        UiRegion,     // 落在已登記的 UI 區域：事件到此為止，不向下滲透給世界層（移動／普攻）
        Rune,         // 右下地脈符印：整段觸控歸符印手勢所有，拖到世界上空也不會變成點地或微彈
        Pip,          // 模式 B 左下微輪盤
        World         // 點地、點目標、模式 A 微彈
    }

    // 螢幕座標一律為 Unity 螢幕座標（原點左下、單位像素）。
    public struct ScreenRegion
    {
        public float XMin;
        public float YMin;
        public float XMax;
        public float YMax;

        public ScreenRegion(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax;
        }

        public bool Contains(float x, float y)
        {
            return x >= XMin && x <= XMax && y >= YMin && y <= YMax;
        }
    }

    // 觸控事件的唯一分流點：每根手指在按下的瞬間被指派一條路由，之後整段觸控生命週期都歸該路由所有。
    // 判定順序即優先序：邊緣死區 → UI 區域 → 符印 → 微輪盤 → 世界。
    // 不依賴 UnityEngine，可直接在 dotnet 下測試。
    public sealed class InputRoutingManager
    {
        public const int MaxUiRegions = 16;

        private readonly ScreenRegion[] _regions = new ScreenRegion[MaxUiRegions];
        private readonly bool[] _regionActive = new bool[MaxUiRegions];
        private int _regionCount;

        private ScreenRegion _pipZone;
        private bool _hasPipZone;

        private ScreenRegion _runeZone;
        private bool _hasRuneZone;
        private ScreenRegion _runeCancelZone;
        private bool _hasRuneCancelZone;

        public float EdgeMarginPixels = GestureMath.EdgeDeadzonePixels;

        // 回傳區域 id（之後用它更新位置、開關、以及辨識哪個區域被點了）；登記滿了回傳 -1。
        public int RegisterUiRegion(ScreenRegion region)
        {
            if (_regionCount >= MaxUiRegions) return -1;
            int id = _regionCount++;
            _regions[id] = region;
            _regionActive[id] = true;
            return id;
        }

        public void UpdateUiRegion(int id, ScreenRegion region)
        {
            if (id < 0 || id >= _regionCount) return;
            _regions[id] = region;
        }

        public void SetUiRegionActive(int id, bool active)
        {
            if (id < 0 || id >= _regionCount) return;
            _regionActive[id] = active;
        }

        public void SetPipZone(ScreenRegion zone)
        {
            _pipZone = zone;
            _hasPipZone = true;
        }

        public void SetRuneZone(ScreenRegion zone)
        {
            _runeZone = zone;
            _hasRuneZone = true;
        }

        // 取消區只在符印拖曳中有意義：它不參與 Route()，平時點在這塊上面仍是一般的世界觸控。
        public void SetRuneCancelZone(ScreenRegion zone)
        {
            _runeCancelZone = zone;
            _hasRuneCancelZone = true;
        }

        public bool IsInRuneCancelZone(float x, float y)
        {
            return _hasRuneCancelZone && _runeCancelZone.Contains(x, y);
        }

        public TouchRoute Route(float x, float y, float screenWidth, float screenHeight, ControlMode mode, out int uiRegionId)
        {
            uiRegionId = -1;

            if (GestureMath.IsInEdgeDeadzone(x, y, screenWidth, screenHeight, EdgeMarginPixels))
                return TouchRoute.Rejected;

            for (int i = 0; i < _regionCount; i++)
            {
                if (_regionActive[i] && _regions[i].Contains(x, y))
                {
                    uiRegionId = i;
                    return TouchRoute.UiRegion;
                }
            }

            if (_hasRuneZone && _runeZone.Contains(x, y))
                return TouchRoute.Rune;

            if (mode == ControlMode.ModeB_DualZonePip && _hasPipZone && _pipZone.Contains(x, y))
                return TouchRoute.Pip;

            return TouchRoute.World;
        }
    }
}
