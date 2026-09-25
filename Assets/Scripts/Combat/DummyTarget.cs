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
        private Renderer[] _allRenderers;
        private TargetOverheadDisplay _overhead;

        // v0.8.0 對抗審查 r1 M1（使用者裁定 2026-09-25，計畫書 §4「不得改既有木樁」的授權例外）：
        // 木樁在 0／1 號塔的南北連線上、不在格點裡，對手從紅方基地走向 0 號會正面卡死。
        // 佔領模式（Lobby／Active／Ended）時停用：全部 Renderer（含頭頂血條）與 Collider 關閉；回單挑（Off）時恢復。
        // 單挑模式從不呼叫 SetCaptureSuppressed，這個旗標恆為 false，行為與 v0.7.0 逐行相同。
        public bool IsCaptureSuppressed { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _block = new MaterialPropertyBlock();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInChildren<Renderer>();
            _visual = _bodyRenderer != null ? _bodyRenderer.transform : transform;
            _visualRestRotation = _visual.localRotation;
            _allRenderers = GetComponentsInChildren<Renderer>(true);
            _overhead = GetComponent<TargetOverheadDisplay>();
            ApplyColor(_baseColor);

            OnDamaged += HandleDamaged;
        }

        public void SetCaptureSuppressed(bool suppressed)
        {
            IsCaptureSuppressed = suppressed;
            // 恢復時若木樁正在死亡倒數，維持倒地時的隱藏狀態，交給 Update 的復活流程打開（同 HandleDeath）。
            bool shown = !suppressed && IsAlive;
            for (int i = 0; i < _allRenderers.Length; i++)
                if (_allRenderers[i] != null) _allRenderers[i].enabled = shown;
            SetCollidersEnabled(shown);
            if (_overhead != null) _overhead.SetHidden(suppressed);
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
                    if (!IsCaptureSuppressed) // 佔領模式中倒數到期：照樣復活補血，但不得把停用的身體打開
                    {
                        if (_bodyRenderer != null) _bodyRenderer.enabled = true;
                        SetCollidersEnabled(true);
                    }
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
