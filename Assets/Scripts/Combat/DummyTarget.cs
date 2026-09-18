using UnityEngine;
using Vow.Core;

namespace Vow.Combat
{
    // 訓練木樁：受擊高亮 (Damage Flash)、受擊晃動、死亡後自動復活，讓測試者能連續單挑 10 分鐘不必重開場景。
    public sealed class DummyTarget : CombatTargetBehaviour, IHitstopParticipant
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Color _baseColor = new Color(0.72f, 0.52f, 0.32f);
        [SerializeField] private Color _flashColor = Color.white;
        [SerializeField] private float _flashSeconds = 0.09f;
        [SerializeField] private float _wobbleSeconds = 0.25f;
        [SerializeField] private float _wobbleDegrees = 9f;
        [SerializeField] private float _respawnSeconds = 2.5f;

        private MaterialPropertyBlock _block;
        private Transform _visual;
        private Quaternion _visualRestRotation;
        private float _flashTimer;
        private float _wobbleTimer;
        private float _respawnTimer;
        private bool _frozen;

        protected override void Awake()
        {
            base.Awake();
            _block = new MaterialPropertyBlock();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInChildren<Renderer>();
            _visual = _bodyRenderer != null ? _bodyRenderer.transform : transform;
            _visualRestRotation = _visual.localRotation;
            ApplyColor(_baseColor);

            OnDamaged += HandleDamaged;
        }

        private void OnDestroy()
        {
            OnDamaged -= HandleDamaged;
        }

        private void HandleDamaged(float amount)
        {
            _flashTimer = _flashSeconds;
            _wobbleTimer = _wobbleSeconds;
            ApplyColor(_flashColor);
        }

        protected override void HandleDeath()
        {
            _respawnTimer = _respawnSeconds;
            if (_bodyRenderer != null) _bodyRenderer.enabled = false;
            SetCollidersEnabled(false); // 看不見的屍體不該擋路，也不該接到點擊
        }

        private void SetCollidersEnabled(bool enabled)
        {
            Collider[] colliders = TargetColliders;
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = enabled;
        }

        public void SetHitstopFrozen(bool frozen)
        {
            _frozen = frozen;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_respawnTimer > 0f)
            {
                _respawnTimer -= dt;
                if (_respawnTimer <= 0f)
                {
                    if (_bodyRenderer != null) _bodyRenderer.enabled = true;
                    SetCollidersEnabled(true);
                    ApplyColor(_baseColor);
                    Revive();
                }
                return;
            }

            if (_frozen) return; // 頓挫幀期間凍結受擊演出，與攻擊者的動畫一起「卡」住

            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                float t = _flashSeconds > 0f ? Mathf.Clamp01(_flashTimer / _flashSeconds) : 0f;
                ApplyColor(Color.Lerp(_baseColor, _flashColor, t));
            }

            if (_wobbleTimer > 0f)
            {
                _wobbleTimer -= dt;
                float t = _wobbleSeconds > 0f ? Mathf.Clamp01(_wobbleTimer / _wobbleSeconds) : 0f;
                float angle = Mathf.Sin((1f - t) * Mathf.PI * 4f) * _wobbleDegrees * t;
                _visual.localRotation = _visualRestRotation * Quaternion.Euler(angle, 0f, 0f);
                if (_wobbleTimer <= 0f) _visual.localRotation = _visualRestRotation;
            }
        }

        private void ApplyColor(Color color)
        {
            if (_bodyRenderer == null) return;
            _bodyRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);   // URP Lit / Unlit
            _block.SetColor(LegacyColorId, color); // 內建管線後備
            _bodyRenderer.SetPropertyBlock(_block);
        }
    }
}
