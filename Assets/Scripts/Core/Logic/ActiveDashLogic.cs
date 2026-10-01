using System;

namespace Vow.Core.Logic
{
    public enum ActiveDashOutcome
    {
        Rejected,      // 此狀態不收主動滑步，或方向為零／身體拒絕；不扣充能、不改狀態
        NoCharge,      // 充能 0：什麼都不動
        CadenceFlick,  // 目押窗口內：等同一次命中連動微彈（進 CadenceDashing、保留目標）
        FreeDash       // 窗口外的自由滑步：大腦狀態不變（前搖會先被打斷）
    }

    // camera-lab 主動滑步鈕（docs/CAMERA_LAB_COMBAT_PLAN.md §1.2）。不依賴 UnityEngine。
    // 充能、回充與 1.0s 連段衰減全部沿用身體端同一個 CadenceSim 狀態——這裡只決定「走哪條既有路徑」，不另開一套。
    public static class ActiveDashLogic
    {
        public const float StickDeadzone = 0.2f;

        // 搖桿（螢幕右＝x、上＝y，已正規化到 0~1）推量達死區即依鏡頭 yaw 旋轉；否則＝鏡頭水平前方。
        // 旋轉與搖桿移動同一換算：Quaternion.Euler(0, yaw, 0) * (x, 0, y)。
        public static void ResolveDirection(float stickX, float stickY, float yawDegrees, out float worldX, out float worldZ)
        {
            double length = Math.Sqrt((double)stickX * stickX + (double)stickY * stickY);
            if (length < StickDeadzone)
            {
                CameraLabAim.GroundForward(yawDegrees, out worldX, out worldZ);
                return;
            }
            double yaw = yawDegrees * Math.PI / 180.0;
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            double x = stickX * c + stickY * s;
            double z = -stickX * s + stickY * c;
            double n = Math.Sqrt(x * x + z * z);
            worldX = (float)(x / n);
            worldZ = (float)(z / n);
        }

        public static ActiveDashOutcome Route(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Idle:
                case PlayerState.Moving:
                case PlayerState.AttackWindup:
                    return ActiveDashOutcome.FreeDash;
                case PlayerState.AttackRelease:
                    return ActiveDashOutcome.CadenceFlick;
                default:
                    return ActiveDashOutcome.Rejected;
            }
        }

        // charges：身體端目前充能（呼叫端已排除縛足／飛行／滑步中）。currentPosition：打斷前搖時的原地移動指令。
        public static ActiveDashOutcome Execute<TTarget>(HeroCombatBrain<TTarget> brain, IHeroBodyPort<TTarget> body,
            int charges, float worldDirX, float worldDirZ, GroundPoint currentPosition) where TTarget : class
        {
            if (brain == null || body == null) return ActiveDashOutcome.Rejected;
            if ((double)worldDirX * worldDirX + (double)worldDirZ * worldDirZ < 1e-12) return ActiveDashOutcome.Rejected;

            ActiveDashOutcome route = Route(brain.State);
            if (route == ActiveDashOutcome.Rejected) return route;
            if (charges <= 0) return ActiveDashOutcome.NoCharge;

            if (route == ActiveDashOutcome.CadenceFlick)
            {
                brain.CommandFlick(worldDirX, worldDirZ);
                return brain.State == PlayerState.CadenceDashing ? ActiveDashOutcome.CadenceFlick : ActiveDashOutcome.Rejected;
            }

            if (brain.State == PlayerState.AttackWindup)
            {
                // 比照「前搖可被移動指令立即打斷」：原地移動指令打斷（不命中、不耗週期、解除鎖定），再停下。
                brain.CommandMove(currentPosition);
                body.StopMoving();
            }
            return body.TryBeginCadenceDash(worldDirX, worldDirZ) ? ActiveDashOutcome.FreeDash : ActiveDashOutcome.Rejected;
        }
    }
}
