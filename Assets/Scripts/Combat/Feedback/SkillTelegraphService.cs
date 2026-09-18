using UnityEngine;
using Vow.Core;

namespace Vow.Combat.Feedback
{
    // ISkillTelegraphService 灰盒版：純 LineRenderer 的高對比指向箭頭與圓形法陣，Awake 預熱、顯示/隱藏只切 enabled。
    // 冷庫協議：不做著色器特效、不做粒子。
    public sealed class SkillTelegraphService : MonoBehaviour, ISkillTelegraphService
    {
        private const int RingSegments = 48;
        private const float GroundOffset = 0.04f;
        private const float SnapSeconds = 0.1f;      // GDD：邊界 0.1 秒內一目了然
        private const float SnapOvershoot = 0.15f;

        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Color _baseColor = new Color(0.1f, 0.95f, 1f, 0.95f);
        [SerializeField] private Color _snapColor = Color.white;

        private LineRenderer _shaft;
        private LineRenderer _head;
        private LineRenderer _ring;

        private Vector3 _origin;
        private Vector3 _direction = Vector3.forward;
        private float _length;
        private float _width;
        private Vector3 _center;
        private float _radius;
        private float _edgeThickness;
        private float _snapRemaining;

        public TelegraphShape ActiveShape { get; private set; }
        public bool IsAiming { get; private set; }

        private void Awake()
        {
            _shaft = CreateLine("TelegraphShaft", 2, false);
            _head = CreateLine("TelegraphHead", 2, false);
            _ring = CreateLine("TelegraphRing", RingSegments, true);
            SetVisible(false, false);
        }

        public void ShowLineIndicator(Vector3 origin, Vector3 direction, float length, float width)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;

            ActiveShape = TelegraphShape.LineCast;
            IsAiming = true;
            _origin = origin;
            _direction = direction.normalized;
            _length = Mathf.Max(0.05f, length);
            _width = Mathf.Max(0.02f, width);

            SetVisible(true, false);
            RebuildLine(1f);
        }

        public void ShowZoneIndicator(Vector3 center, float radius, float edgeThickness)
        {
            ActiveShape = TelegraphShape.ZoneCast;
            IsAiming = true;
            _center = center;
            _radius = Mathf.Max(0.05f, radius);
            _edgeThickness = Mathf.Max(0.02f, edgeThickness);

            SetVisible(false, true);
            RebuildRing(1f);
        }

        // 線性：原點不動，方向指向瞄準點；法陣：圓心跟著瞄準點走。
        public void UpdateAimTransform(Vector3 currentAimPosition)
        {
            if (!IsAiming) return;

            if (ActiveShape == TelegraphShape.LineCast)
            {
                Vector3 toAim = currentAimPosition - _origin;
                toAim.y = 0f;
                if (toAim.sqrMagnitude > 1e-6f) _direction = toAim.normalized;
                RebuildLine(CurrentSnapScale());
            }
            else
            {
                _center = currentAimPosition;
                RebuildRing(CurrentSnapScale());
            }
        }

        // 線性指示器的原點跟著施法者移動時使用（不改方向與長度）。
        public void UpdateLineOrigin(Vector3 origin)
        {
            if (!IsAiming || ActiveShape != TelegraphShape.LineCast) return;
            _origin = origin;
            RebuildLine(CurrentSnapScale());
        }

        public void HideIndicator()
        {
            IsAiming = false;
            _snapRemaining = 0f;
            SetVisible(false, false);
        }

        public void TriggerSnapFeedback()
        {
            if (!IsAiming) return;
            _snapRemaining = SnapSeconds;
        }

        private void Update()
        {
            if (_snapRemaining <= 0f) return;

            _snapRemaining -= Time.deltaTime;
            float t = Mathf.Clamp01(_snapRemaining / SnapSeconds);
            Color color = Color.Lerp(_baseColor, _snapColor, t);
            ApplyColor(color);

            if (ActiveShape == TelegraphShape.LineCast) RebuildLine(CurrentSnapScale());
            else RebuildRing(CurrentSnapScale());
        }

        // 外衝吸附：先放大 15%，0.1 秒內收回原尺寸。
        private float CurrentSnapScale()
        {
            if (_snapRemaining <= 0f) return 1f;
            return 1f + SnapOvershoot * Mathf.Clamp01(_snapRemaining / SnapSeconds);
        }

        private void RebuildLine(float scale)
        {
            float headLength = Mathf.Min(_length * 0.35f, _width * 2.5f);
            float length = _length * scale;
            Vector3 lift = Vector3.up * GroundOffset;
            Vector3 tip = _origin + _direction * length + lift;
            Vector3 neck = _origin + _direction * (length - headLength) + lift;

            _shaft.startWidth = _width;
            _shaft.endWidth = _width;
            _shaft.SetPosition(0, _origin + lift);
            _shaft.SetPosition(1, neck);

            _head.startWidth = _width * 2.6f;
            _head.endWidth = 0f;
            _head.SetPosition(0, neck);
            _head.SetPosition(1, tip);
        }

        private void RebuildRing(float scale)
        {
            float radius = _radius * scale;
            _ring.startWidth = _edgeThickness;
            _ring.endWidth = _edgeThickness;

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                _ring.SetPosition(i, new Vector3(
                    _center.x + Mathf.Cos(angle) * radius,
                    _center.y + GroundOffset,
                    _center.z + Mathf.Sin(angle) * radius));
            }
        }

        private LineRenderer CreateLine(string objectName, int positions, bool loop)
        {
            GameObject go = new GameObject(objectName);
            go.layer = 2; // Ignore Raycast
            go.transform.SetParent(transform, false);

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = positions;
            line.alignment = LineAlignment.TransformZ; // 配合下方旋轉：線寬攤平在地面上，俯視鏡頭下寬度穩定
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (_lineMaterial != null) line.sharedMaterial = _lineMaterial;
            line.startColor = _baseColor;
            line.endColor = _baseColor;

            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return line;
        }

        private void ApplyColor(Color color)
        {
            _shaft.startColor = color; _shaft.endColor = color;
            _head.startColor = color; _head.endColor = color;
            _ring.startColor = color; _ring.endColor = color;
        }

        private void SetVisible(bool line, bool ring)
        {
            _shaft.enabled = line;
            _head.enabled = line;
            _ring.enabled = ring;
            if (line || ring) ApplyColor(_baseColor);
        }
    }
}
