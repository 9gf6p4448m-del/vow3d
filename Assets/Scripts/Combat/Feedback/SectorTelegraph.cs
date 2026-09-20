using UnityEngine;

namespace Vow.Combat.Feedback
{
    // 擴散火浪的扇形預警（PHASE2_BATCH4_PLAN.md §4-6）。自有 LineRenderer，**不經 ISkillTelegraphService**。
    //
    // 否決「借用 SkillTelegraphService」的理由：`TelegraphShape`（ISkillTelegraphService.cs:5-9）是
    // `ARCHITECTURE.md:206-210` 的逐字契約、不得加 `SectorCast`；而該服務只有三條 LineRenderer
    // （欄位 `:19-21`、`Awake` `:35-41` 建），要同時畫兩條邊＋圓弧得加第 4、5 條線並改
    // `ShowLineIndicator` 的語意，等於改一個 Phase 1 已驗收過的元件。新開只加不改。
    //
    // 零配置：頂點陣列在 Awake 預配一次，之後只 `SetPositions`。
    [DisallowMultipleComponent]
    public sealed class SectorTelegraph : MonoBehaviour
    {
        private const int ArcSegments = 16;
        private const float GroundHeight = 0.05f;

        [SerializeField] private LineRenderer _line;

        // 兩條邊＋圓弧＝頂點 → 弧起點 → …弧上 17 點… → 頂點（收筆回頂點，兩條邊各畫一次）
        private readonly Vector3[] _points = new Vector3[ArcSegments + 3];
        private float _remainingSeconds;

        public int ShowCount { get; private set; }
        public bool IsVisible => _line != null && _line.enabled;

        private void Awake()
        {
            if (_line == null) _line = GetComponent<LineRenderer>();
            if (_line == null) return;
            _line.useWorldSpace = true;
            _line.positionCount = _points.Length;
            _line.enabled = false;
        }

        // apex＝英雄本體；direction＝英雄面向（水平）；angle／range 讀 ElementTuning。
        public void Show(Vector3 apex, Vector3 direction, float rangeMeters, float totalAngleDegrees,
                         float durationSeconds)
        {
            if (_line == null) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
            direction.Normalize();

            float half = totalAngleDegrees * 0.5f;
            float baseAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            Vector3 origin = new Vector3(apex.x, GroundHeight, apex.z);

            _points[0] = origin;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float t = (float)i / ArcSegments;
                float degrees = (baseAngle - half) + totalAngleDegrees * t;
                float radians = degrees * Mathf.Deg2Rad;
                _points[i + 1] = new Vector3(origin.x + Mathf.Sin(radians) * rangeMeters,
                                             GroundHeight,
                                             origin.z + Mathf.Cos(radians) * rangeMeters);
            }
            _points[_points.Length - 1] = origin;

            _line.SetPositions(_points);
            _line.enabled = true;
            _remainingSeconds = durationSeconds;
            ShowCount++;
        }

        public void Hide()
        {
            _remainingSeconds = 0f;
            if (_line != null) _line.enabled = false;
        }

        private void Update()
        {
            if (_remainingSeconds <= 0f) return;
            _remainingSeconds -= Time.deltaTime;
            if (_remainingSeconds <= 0f) Hide();
        }
    }
}
