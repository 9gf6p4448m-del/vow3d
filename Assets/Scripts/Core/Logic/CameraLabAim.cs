using System;

namespace Vow.Core.Logic
{
    // camera-lab 第三人稱的準星數學（GDD §貳.4 模式 C 的 ATK 輔助瞄準／§參.1 石牆朝向）。不依賴 UnityEngine。
    // 鏡頭旋轉為 Euler(pitch, yaw, 0)：視線＝(sin yaw·cos pitch, −sin pitch, cos yaw·cos pitch)，水平前方＝(sin yaw, cos yaw)。
    public static class CameraLabAim
    {
        public const float ConeHalfAngleDegrees = 30f;   // 灰盒暫定
        public const float MaxAimDistance = 8f;          // 灰盒暫定

        private const double DegToRad = Math.PI / 180.0;

        public static void ViewForward(float yawDegrees, float pitchDegrees, out float x, out float y, out float z)
        {
            double yaw = yawDegrees * DegToRad;
            double pitch = pitchDegrees * DegToRad;
            double cp = Math.Cos(pitch);
            x = (float)(Math.Sin(yaw) * cp);
            y = (float)(-Math.Sin(pitch));
            z = (float)(Math.Cos(yaw) * cp);
        }

        public static void GroundForward(float yawDegrees, out float x, out float z)
        {
            double yaw = yawDegrees * DegToRad;
            x = (float)Math.Sin(yaw);
            z = (float)Math.Cos(yaw);
        }

        // 塑牆拖曳方向（RuneCaster／RuneGhostPreview 的生產路徑都經過這裡）：
        // THIRD 只看拉伸量、方向固定為準星水平前方 (aimX, aimZ)；TOP 原樣交回螢幕換算出的方向（位元相同）。
        public static void WallDragDirection(bool thirdPerson, float aimX, float aimZ, float screenWorldX, float screenWorldZ,
            out float x, out float z)
        {
            if (!thirdPerson)
            {
                x = screenWorldX;
                z = screenWorldZ;
                return;
            }
            x = aimX;
            z = aimZ;
        }
    }

    // 準星錐挑目標：串流式（Begin 後逐一 Consider），值型別、零配置。
    // 規則：水平夾角 ≤ 半角、水平距離 ≤ 上限；取夾角最小，夾角相同（cos 差 < 1e-4）取較近。
    // 錐內沒有候選時退回距離上限內最近者（不看角度；距離相同取先 Consider 者）＝ResolvedIndex。
    // 黏性：呼叫端把「正在打的目標」標成 preferred。它在錐內時，他人夾角要小超過 StickyMarginDegrees 才換；
    // 它在距離內但錐外時，錐內有人就換（瞄準覆寫黏性）、錐內沒人就留著（不因另一個較近的敵人中途改打）。
    // fallbackEligible=false（呼叫端判定視線被牆擋）的候選不參加「錐外退回最近者」；錐內挑選與 preferred 不受影響。
    // 武器多載 Begin(..., WeaponSpec)（v0.16.0 武器灰盒）：錐半角 ≤ 0＝沒有錐（只剩距離內最近者，preferred 忽略，見下行）；
    // FallbackToNearest=false＝錐外一律不挑（含 preferred）。既有 6 參數 Begin＝有錐、可退回，行為不變。
    // 無錐武器（劍）不吃黏性：preferred 一律忽略，永遠取距離內最近（覆審 r1 M3，主對話裁定＝照凍結 W2 字面）。
    public struct AimTargetPicker
    {
        public const float StickyMarginDegrees = 15f;   // 灰盒暫定

        private const float CosTieEpsilon = 1e-4f;

        private float _originX, _originZ, _aimX, _aimZ, _cosLimit, _maxDistance;
        private float _bestCos, _bestDistance, _nearestDistance, _preferredCos;
        private bool _preferredInCone;
        private bool _coneEnabled, _fallbackToNearest;

        public int BestIndex { get; private set; }
        public int NearestIndex { get; private set; }
        public int PreferredIndex { get; private set; }   // 距離內的 preferred；-1＝沒有或超出距離

        public int ResolvedIndex
        {
            get
            {
                if (BestIndex >= 0)
                {
                    if (_preferredInCone && PreferredIndex != BestIndex)
                    {
                        double bestAngle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, _bestCos)));
                        double preferredAngle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, _preferredCos)));
                        if (preferredAngle <= bestAngle + StickyMarginDegrees * Math.PI / 180.0) return PreferredIndex;
                    }
                    return BestIndex;
                }
                if (!_fallbackToNearest) return -1;
                return PreferredIndex >= 0 ? PreferredIndex : NearestIndex;
            }
        }

        public void Begin(float originX, float originZ, float aimX, float aimZ, float coneHalfAngleDegrees, float maxDistance)
        {
            double length = Math.Sqrt((double)aimX * aimX + (double)aimZ * aimZ);
            _originX = originX;
            _originZ = originZ;
            _aimX = length > 1e-9 ? (float)(aimX / length) : 0f;
            _aimZ = length > 1e-9 ? (float)(aimZ / length) : 0f;
            _cosLimit = (float)Math.Cos(coneHalfAngleDegrees * Math.PI / 180.0);
            _maxDistance = maxDistance;
            _bestCos = float.NegativeInfinity;
            _bestDistance = float.PositiveInfinity;
            _nearestDistance = float.PositiveInfinity;
            _preferredInCone = false;
            _preferredCos = float.NegativeInfinity;
            BestIndex = -1;
            NearestIndex = -1;
            PreferredIndex = -1;
            _coneEnabled = true;
            _fallbackToNearest = true;
        }

        public void Begin(float originX, float originZ, float aimX, float aimZ, in WeaponSpec weapon)
        {
            Begin(originX, originZ, aimX, aimZ, weapon.ConeHalfAngleDegrees, weapon.AimRangeMeters);
            _coneEnabled = weapon.ConeHalfAngleDegrees > 0f;
            _fallbackToNearest = weapon.FallbackToNearest;
        }

        // 回傳這個候選是否落在準星錐內（不論是否成為最佳）。
        public bool Consider(int index, float targetX, float targetZ, bool isPreferred = false, bool fallbackEligible = true)
        {
            if (_aimX == 0f && _aimZ == 0f) return false;
            float dx = targetX - _originX;
            float dz = targetZ - _originZ;
            double distance = Math.Sqrt((double)dx * dx + (double)dz * dz);
            if (distance > _maxDistance) return false;
            float d = (float)distance;
            if (fallbackEligible && d < _nearestDistance)
            {
                _nearestDistance = d;
                NearestIndex = index;
            }
            float cos = distance < 1e-6 ? 1f : (float)((dx * _aimX + dz * _aimZ) / distance);
            if (isPreferred && _coneEnabled)
            {
                PreferredIndex = index;
                if (cos >= _cosLimit)
                {
                    _preferredInCone = true;
                    _preferredCos = cos;
                }
            }
            if (!_coneEnabled || cos < _cosLimit) return false;

            bool better = cos > _bestCos + CosTieEpsilon
                          || (cos >= _bestCos - CosTieEpsilon && d < _bestDistance);
            if (better)
            {
                _bestCos = cos;
                _bestDistance = d;
                BestIndex = index;
            }
            return true;
        }
    }
}
