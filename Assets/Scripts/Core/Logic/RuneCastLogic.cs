using System;

namespace Vow.Core.Logic
{
    // 石牆落點：中心（XZ 平面）與牆面法線。石牆的長邊垂直於法線。
    public struct RuneWallPlacement
    {
        public float CenterX;
        public float CenterZ;
        public float NormalX;
        public float NormalZ;
    }

    // 符印施法的冷卻與落點換算（GDD §參-1）。不依賴 UnityEngine。
    public sealed class RuneCastLogic
    {
        private readonly RuneTuning _tuning;
        private double _readyTime = double.NegativeInfinity;

        public RuneCastLogic(RuneTuning tuning)
        {
            _tuning = tuning;
        }

        public bool IsReady(double now) => now >= _readyTime;

        public double CooldownRemaining(double now) => IsReady(now) ? 0.0 : _readyTime - now;

        public void ResetCooldown() { _readyTime = double.NegativeInfinity; }

        // 只有真的要成牆時才呼叫；取消施法不經過這裡，所以不消耗冷卻。
        public bool TryBeginCast(double now)
        {
            if (!IsReady(now)) return false;
            _readyTime = now + _tuning.CooldownSeconds;
            return true;
        }

        // 極速石牆：正前方固定距離的橫向掩體，牆面垂直於英雄朝向。朝向為零向量時回傳 false。
        public bool TryQuickCastPlacement(float posX, float posZ, float forwardX, float forwardZ, out RuneWallPlacement placement)
        {
            return TryPlace(posX, posZ, forwardX, forwardZ, _tuning.QuickCastDistance, out placement);
        }

        // 拖曳施法：方向＝石牆相對英雄的方位；拉伸量 0~1 的前一段（貼身帶）一律＝最近距離，之後線性拉到最遠；
        // 牆面永遠垂直於「英雄→落點」連線。貼身帶讓「把牆放在身邊」不必把手指停在取消圈的邊緣上。
        public bool TryDragPlacement(float posX, float posZ, float dirX, float dirZ, float distance01, out RuneWallPlacement placement)
        {
            float t = distance01 < 0f ? 0f : (distance01 > 1f ? 1f : distance01);
            float band = _tuning.DragNearBand01;
            float u = band < 1f ? (t - band) / (1f - band) : 0f;
            if (u < 0f) u = 0f;
            float distance = _tuning.DragMinDistance + (_tuning.DragMaxDistance - _tuning.DragMinDistance) * u;
            return TryPlace(posX, posZ, dirX, dirZ, distance, out placement);
        }

        private static bool TryPlace(float posX, float posZ, float dirX, float dirZ, float distance, out RuneWallPlacement placement)
        {
            placement = default;
            double length = Math.Sqrt((double)dirX * dirX + (double)dirZ * dirZ);
            if (length < 1e-6) return false;

            float nx = (float)(dirX / length);
            float nz = (float)(dirZ / length);
            placement.CenterX = posX + nx * distance;
            placement.CenterZ = posZ + nz * distance;
            placement.NormalX = nx;
            placement.NormalZ = nz;
            return true;
        }
    }

    // 全隊石牆名冊：依成形順序記住還活著的牆，超過上限時點名最早的那面去坍塌。固定容量、無配置。
    public sealed class RuneWallRoster
    {
        private readonly int[] _slots;
        private int _count;

        public RuneWallRoster(int cap)
        {
            _slots = new int[cap < 1 ? 1 : cap];
        }

        public int Count => _count;

        // 回傳必須坍塌的舊牆 slot；沒有超額則回傳 -1。
        public int Add(int slot)
        {
            int evicted = -1;
            if (_count == _slots.Length)
            {
                evicted = _slots[0];
                RemoveAt(0);
            }
            _slots[_count++] = slot;
            return evicted;
        }

        // 壽命到、被打碎的牆要自己退出名冊，不佔名額。
        public void Remove(int slot)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_slots[i] != slot) continue;
                RemoveAt(i);
                return;
            }
        }

        private void RemoveAt(int index)
        {
            for (int i = index; i < _count - 1; i++) _slots[i] = _slots[i + 1];
            _count--;
        }
    }
}
