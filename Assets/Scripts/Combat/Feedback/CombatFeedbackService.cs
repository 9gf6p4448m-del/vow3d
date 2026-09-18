using System.Collections.Generic;
using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat.Feedback
{
    // ICombatFeedbackService 灰盒版。所有視覺物件於 Awake 預熱，運行期零 Instantiate、零 GC。
    public sealed class CombatFeedbackService : MonoBehaviour, ICombatFeedbackService
    {
        private const int DecalPoolSize = 12;
        private const float MinHitstopMs = 30f;
        private const float MaxHitstopMs = 60f;

        // 防光敏：單次閃白不透明度上限、兩次閃白最短間隔（每秒不超過 3 次）、單次最長持續時間
        private const float MaxFlashAlpha = 0.35f;
        private const float MinFlashIntervalSeconds = 0.34f;
        private const float MaxFlashSeconds = 0.12f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        [Header("震屏：掛在鏡頭的父節點上，跟隨邏輯只動更上層的 Rig，兩者互不干擾")]
        [SerializeField] private Transform _shakePivot;
        [SerializeField] private float _maxShakeDegrees = 2.2f;
        [SerializeField] private float _maxShakeOffset = 0.22f;
        [SerializeField] private float _rotationFrequency = 27f;
        [SerializeField] private float _translationFrequency = 19f;

        [Header("閃白與貼花（材質由 SceneBuilder 指派，須為透明 Unlit）")]
        [SerializeField] private Camera _camera;
        [SerializeField] private Material _flashMaterial;
        [SerializeField] private Material _decalMaterial;

        private readonly List<IHitstopParticipant> _hitstopParticipants = new List<IHitstopParticipant>(8);
        private readonly Renderer[] _decalRenderers = new Renderer[DecalPoolSize];
        private readonly float[] _decalLifetimes = new float[DecalPoolSize];
        private readonly float[] _decalRemaining = new float[DecalPoolSize];
        private readonly Color[] _decalColors = new Color[DecalPoolSize];

        private TraumaShake _shake;
        private MaterialPropertyBlock _block;
        private float _noiseSeed;

        private float _hitstopRemaining;
        private bool _hitstopActive;

        private Renderer _flashRenderer;
        private Color _flashColor;
        private float _flashDuration;
        private float _flashRemaining;
        private float _lastFlashTime = -10f;

        private int _nextDecal;

        public bool IsHitstopActive => _hitstopActive;
        public float CurrentTrauma => _shake.Trauma;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _noiseSeed = Random.value * 100f;
            if (_camera == null) _camera = Camera.main;

            PrewarmFlashQuad();
            PrewarmDecals();
        }

        public void RegisterHitstopParticipant(IHitstopParticipant participant)
        {
            if (participant != null && !_hitstopParticipants.Contains(participant)) _hitstopParticipants.Add(participant);
        }

        // ───────────────────────── ICombatFeedbackService ─────────────────────────

        public void RequestCameraShake(float trauma, float duration = 0.2f)
        {
            _shake.Request(trauma, duration);
        }

        // 只凍結登記者的動畫播放，不動 Time.timeScale：輸入取樣與 220ms 目押窗口必須照常計時，
        // 否則頓挫幀會把玩家的有效反應時間吃掉。
        public void TriggerHitstop(float durationMs)
        {
            float clamped = Mathf.Clamp(durationMs, MinHitstopMs, MaxHitstopMs);
            _hitstopRemaining = Mathf.Max(_hitstopRemaining, clamped * 0.001f);
            if (_hitstopActive) return;

            _hitstopActive = true;
            SetParticipantsFrozen(true);
        }

        // 目押成功切後搖時立即解除頓挫：滑步動畫不得被上一刀的頓挫幀卡住。
        public void CancelHitstop()
        {
            if (!_hitstopActive) return;
            _hitstopActive = false;
            _hitstopRemaining = 0f;
            SetParticipantsFrozen(false);
        }

        public void TriggerScreenFlash(Color flashColor, float durationMs = 50f)
        {
            if (_flashRenderer == null) return;

            float now = Time.unscaledTime;
            if (now - _lastFlashTime < MinFlashIntervalSeconds) return; // 防光敏：頻率上限
            _lastFlashTime = now;

            flashColor.a = Mathf.Min(flashColor.a, MaxFlashAlpha);
            _flashColor = flashColor;
            _flashDuration = Mathf.Clamp(durationMs * 0.001f, 0.01f, MaxFlashSeconds);
            _flashRemaining = _flashDuration;

            FitFlashQuadToCamera();
            _flashRenderer.enabled = true;
        }

        public void SpawnGroundDecal(Vector3 worldPosition, DecalType type, float duration = 3.0f)
        {
            int slot = _nextDecal;
            _nextDecal = (_nextDecal + 1) % DecalPoolSize; // 池滿時覆蓋最舊的一枚

            Renderer decal = _decalRenderers[slot];
            if (decal == null) return;

            Transform decalTransform = decal.transform;
            decalTransform.position = new Vector3(worldPosition.x, 0.02f + slot * 0.0005f, worldPosition.z); // 逐枚微抬，避免重疊時 z-fighting
            decalTransform.rotation = Quaternion.Euler(90f, _noiseSeed * 37f + slot * 53f, 0f);

            _decalColors[slot] = ColorFor(type);
            _decalLifetimes[slot] = Mathf.Max(0.1f, duration);
            _decalRemaining[slot] = _decalLifetimes[slot];
            decal.enabled = true;
        }

        // ───────────────────────── 每幀 ─────────────────────────

        private void Update()
        {
            float unscaledDt = Time.unscaledDeltaTime;

            if (_hitstopActive)
            {
                _hitstopRemaining -= unscaledDt;
                if (_hitstopRemaining <= 0f) CancelHitstop();
            }

            if (_flashRemaining > 0f)
            {
                _flashRemaining -= unscaledDt;
                Color color = _flashColor;
                color.a *= Mathf.Clamp01(_flashRemaining / _flashDuration);
                SetColor(_flashRenderer, color);
                if (_flashRemaining <= 0f) _flashRenderer.enabled = false;
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < DecalPoolSize; i++)
            {
                if (_decalRemaining[i] <= 0f) continue;

                _decalRemaining[i] -= dt;
                // 壽命最後 30% 平滑溶解
                float fade = Mathf.Clamp01(_decalRemaining[i] / (_decalLifetimes[i] * 0.3f));
                Color color = _decalColors[i];
                color.a *= fade;
                SetColor(_decalRenderers[i], color);
                if (_decalRemaining[i] <= 0f) _decalRenderers[i].enabled = false;
            }
        }

        // 震屏放在 LateUpdate：疊加在跟隨鏡頭「之後」，不會被跟隨邏輯覆寫。
        private void LateUpdate()
        {
            if (_shakePivot == null) return;

            _shake.Tick(Time.unscaledDeltaTime);
            float intensity = _shake.Intensity; // = trauma²
            if (intensity <= 0f)
            {
                _shakePivot.localPosition = Vector3.zero;
                _shakePivot.localRotation = Quaternion.identity;
                return;
            }

            // 旋轉與平移各用一組頻率與取樣軸，三軸彼此不相關
            float tr = Time.unscaledTime * _rotationFrequency;
            float tt = Time.unscaledTime * _translationFrequency;
            float pitch = Noise(tr, 0f) * _maxShakeDegrees * intensity;
            float yaw = Noise(tr, 11f) * _maxShakeDegrees * intensity;
            float roll = Noise(tr, 23f) * _maxShakeDegrees * intensity;
            float offsetX = Noise(tt, 37f) * _maxShakeOffset * intensity;
            float offsetY = Noise(tt, 53f) * _maxShakeOffset * intensity;

            _shakePivot.localRotation = Quaternion.Euler(pitch, yaw, roll);
            _shakePivot.localPosition = new Vector3(offsetX, offsetY, 0f);
        }

        private float Noise(float time, float channel)
        {
            return Mathf.PerlinNoise(_noiseSeed + channel, time) * 2f - 1f;
        }

        // ───────────────────────── 預熱 ─────────────────────────

        private void PrewarmFlashQuad()
        {
            if (_camera == null) return;

            GameObject quad = QuadMeshFactory.Create("ScreenFlash", _flashMaterial);
            quad.transform.SetParent(_camera.transform, false);
            _flashRenderer = quad.GetComponent<Renderer>();
            _flashRenderer.enabled = false;
        }

        private void FitFlashQuadToCamera()
        {
            float distance = _camera.nearClipPlane + 0.05f;
            float height = _camera.orthographic
                ? _camera.orthographicSize * 2f
                : 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            Transform quad = _flashRenderer.transform;
            quad.localPosition = new Vector3(0f, 0f, distance);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(height * _camera.aspect * 1.1f, height * 1.1f, 1f);
        }

        private void PrewarmDecals()
        {
            Transform poolRoot = new GameObject("DecalPool").transform;
            poolRoot.SetParent(transform, false);

            for (int i = 0; i < DecalPoolSize; i++)
            {
                GameObject quad = QuadMeshFactory.Create("GroundDecal", _decalMaterial);
                quad.transform.SetParent(poolRoot, false);
                quad.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
                _decalRenderers[i] = quad.GetComponent<Renderer>();
                _decalRenderers[i].enabled = false;
            }
        }

        private void SetParticipantsFrozen(bool frozen)
        {
            for (int i = 0; i < _hitstopParticipants.Count; i++) _hitstopParticipants[i].SetHitstopFrozen(frozen);
        }

        private void SetColor(Renderer target, Color color)
        {
            target.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(LegacyColorId, color);
            target.SetPropertyBlock(_block);
        }

        private static Color ColorFor(DecalType type)
        {
            switch (type)
            {
                case DecalType.ScorchCrater: return new Color(0.12f, 0.07f, 0.04f, 0.85f);
                case DecalType.FrostCrack: return new Color(0.62f, 0.86f, 1f, 0.8f);
                default: return new Color(0.34f, 0.12f, 0.5f, 0.85f);
            }
        }
    }
}
