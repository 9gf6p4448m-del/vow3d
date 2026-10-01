using System;

namespace Vow.Core.Logic
{
    // camera-lab 第三人稱的準星數學（docs/CAMERA_LAB_COMBAT_PLAN.md §1.1／§1.3）。不依賴 UnityEngine。
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

        // 塑牆拖曳方向：THIRD 只看拉伸量、方向固定為鏡頭水平前方；TOP 原樣交回螢幕換算出的方向（位元相同）。
        public static void WallDragDirection(bool thirdPerson, float yawDegrees, float screenWorldX, float screenWorldZ,
            out float x, out float z)
        {
            if (!thirdPerson)
            {
                x = screenWorldX;
                z = screenWorldZ;
                return;
            }
            GroundForward(yawDegrees, out x, out z);
        }
    }

    // 準星錐挑目標：串流式（Begin 後逐一 Consider），值型別、零配置。
    // 規則：水平夾角 ≤ 半角、水平距離 ≤ 上限；取夾角最小，夾角相同（cos 差 < 1e-4）取較近。
    public struct AimTargetPicker
    {
        private const float CosTieEpsilon = 1e-4f;

        private float _originX, _originZ, _aimX, _aimZ, _cosLimit, _maxDistance;
        private float _bestCos, _bestDistance;

        public int BestIndex { get; private set; }

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
            BestIndex = -1;
        }

        // 回傳這個候選是否落在準星錐內（不論是否成為最佳）。
        public bool Consider(int index, float targetX, float targetZ)
        {
            if (_aimX == 0f && _aimZ == 0f) return false;
            float dx = targetX - _originX;
            float dz = targetZ - _originZ;
            double distance = Math.Sqrt((double)dx * dx + (double)dz * dz);
            if (distance > _maxDistance) return false;
            float cos = distance < 1e-6 ? 1f : (float)((dx * _aimX + dz * _aimZ) / distance);
            if (cos < _cosLimit) return false;

            float d = (float)distance;
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
