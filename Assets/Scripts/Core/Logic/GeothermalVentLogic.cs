namespace Vow.Core.Logic
{
    // v0.14.0 地熱點（docs/V0140_CANYON_PLAN.md §4.5，2026-09-30 凍結）。零 UnityEngine、固定 2 點、零配置。
    // 邏輯層不追蹤飛行、存活與對局狀態：heroCanTrigger＝「玩家英雄存活 && 未在移動鎖定（含地熱點飛行）&& 對局 Active」，
    // 由呼叫端每 tick 算好傳入。只有玩家操控的英雄會呼叫 Tick——AI 對手、先鋒、巨獸在結構上不會觸發。
    //
    // Tick 順序（對每一點依索引 0、1）：
    //   ① cooldown = max(0, cooldown − dt)
    //   ② cooldown > 0 → progress = 0，本點結束
    //   ③ 本 tick 有待處理的受傷旗標 → progress = 0，本點結束（旗標只作用於本 tick）
    //   ④ 英雄可觸發且在圓內（含邊界）→ progress += dt，否則 progress = 0
    //   ⑤ progress ≥ VentChannelSeconds → 發射、progress = 0、cooldown = VentCooldownSeconds
    public sealed class GeothermalVentLogic
    {
        private readonly CanyonTerrainSpec _terrain;
        private readonly float _padRadiusSquared;
        private readonly float _channelSeconds;
        private readonly float _cooldownSeconds;
        private readonly float[] _progress = new float[CanyonTerrainSpec.VentCount];
        private readonly float[] _cooldown = new float[CanyonTerrainSpec.VentCount];
        private bool _damagePending;

        public GeothermalVentLogic(CanyonTerrainSpec terrain, CanyonTuning tuning)
        {
            _terrain = terrain;
            _padRadiusSquared = tuning.VentPadRadius * tuning.VentPadRadius;
            _channelSeconds = tuning.VentChannelSeconds;
            _cooldownSeconds = tuning.VentCooldownSeconds;
            LastLaunchPad = -1;
        }

        public int LaunchCount { get; private set; }
        public int LastLaunchPad { get; private set; }
        public float LastLandingX { get; private set; }
        public float LastLandingZ { get; private set; }

        public float Progress(int pad) => _progress[pad];
        public float Cooldown(int pad) => _cooldown[pad];

        public void NotifyDamaged()
        {
            _damagePending = true;
        }

        public void Tick(float dt, float heroX, float heroZ, bool heroCanTrigger)
        {
            bool damaged = _damagePending;
            _damagePending = false;

            for (int pad = 0; pad < CanyonTerrainSpec.VentCount; pad++)
            {
                float next = _cooldown[pad] - dt;
                _cooldown[pad] = next > 0f ? next : 0f;                             // ①
                if (_cooldown[pad] > 0f)                                             // ②
                {
                    _progress[pad] = 0f;
                    continue;
                }
                if (damaged)                                                         // ③
                {
                    _progress[pad] = 0f;
                    continue;
                }
                if (heroCanTrigger && IsOnPad(pad, heroX, heroZ)) _progress[pad] += dt;   // ④
                else _progress[pad] = 0f;
                if (_progress[pad] >= _channelSeconds)                               // ⑤
                {
                    _progress[pad] = 0f;
                    _cooldown[pad] = _cooldownSeconds;
                    LaunchCount++;
                    LastLaunchPad = pad;
                    LastLandingX = _terrain.VentLandingX(pad);
                    LastLandingZ = _terrain.VentLandingZ(pad);
                }
            }
        }

        // 第二局：兩點冷卻、引導與受傷旗標歸 0（LaunchCount 等診斷計數不歸零）。
        public void Reset()
        {
            for (int pad = 0; pad < CanyonTerrainSpec.VentCount; pad++)
            {
                _progress[pad] = 0f;
                _cooldown[pad] = 0f;
            }
            _damagePending = false;
        }

        private bool IsOnPad(int pad, float x, float z)
        {
            float dx = x - _terrain.VentPadX(pad);
            float dz = z - _terrain.VentPadZ(pad);
            return dx * dx + dz * dz <= _padRadiusSquared;
        }
    }
}
