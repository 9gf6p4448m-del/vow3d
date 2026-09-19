using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 英雄頭上的護盾條。整條在 Awake 預建（QuadMeshFactory，執行期禁止 CreatePrimitive：IL2CPP 會剔除
    // 沒被引用的 MeshCollider），戰鬥中只改 localScale 與 Renderer.enabled，零配置。
    // 刻意不掛在英雄底下、也不 DontDestroyOnLoad：它必須跟場景同生共死（V5）。
    public sealed class HeroShieldBar : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Material _barMaterial;
        [SerializeField] private float _height = 2.3f;
        [SerializeField] private float _barWidth = 1.2f;
        [SerializeField] private float _barThickness = 0.12f;

        private Transform _root;
        private Transform _fill;
        private Renderer _fillRenderer;
        private Renderer _backgroundRenderer;
        private Transform _cameraTransform;
        private MaterialPropertyBlock _block;

        private IRockShield _shield;
        private float _fullAmount = 1f;

        // batchmode 沒有畫面，isVisible 恆假；可見與否一律看 enabled／activeInHierarchy。
        public bool IsVisible => _fillRenderer != null && _fillRenderer.enabled
                                 && _root != null && _root.gameObject.activeInHierarchy;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();

            _root = new GameObject(name + "_ShieldBar").transform;
            _root.position = transform.position + new Vector3(0f, _height, 0f);

            CreateBarQuad("ShieldBarBackground", new Color(0.06f, 0.08f, 0.12f), 0.002f, out Transform background);
            background.localScale = new Vector3(_barWidth + 0.06f, _barThickness + 0.06f, 1f);
            CreateBarQuad("ShieldBarFill", new Color(0.45f, 0.85f, 1f), 0f, out _fill);
            _backgroundRenderer = background.GetComponent<Renderer>();
            _fillRenderer = _fill.GetComponent<Renderer>();

            SetBarVisible(false);
        }

        private void Start()
        {
            Camera main = Camera.main;
            if (main != null) _cameraTransform = main.transform;
        }

        public void Initialize(IRockShield shield, ProjectileTuning tuning)
        {
            _shield = shield;
            _fullAmount = tuning != null && tuning.ShieldAmount > 0f ? tuning.ShieldAmount : 1f;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }

        private void CreateBarQuad(string objectName, Color color, float depthOffset, out Transform quadTransform)
        {
            GameObject quad = QuadMeshFactory.Create(objectName, _barMaterial);

            quadTransform = quad.transform;
            quadTransform.SetParent(_root, false);
            quadTransform.localPosition = new Vector3(0f, 0f, depthOffset);

            Renderer quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(LegacyColorId, color);
            quadRenderer.SetPropertyBlock(_block);
        }

        private void LateUpdate()
        {
            _root.position = transform.position + new Vector3(0f, _height, 0f);
            if (_cameraTransform != null) _root.rotation = _cameraTransform.rotation;

            float amount = _shield != null ? _shield.Amount : 0f;
            if (amount <= 0f)
            {
                SetBarVisible(false);
                return;
            }

            SetBarVisible(true);
            float normalized = Mathf.Clamp01(amount / _fullAmount);
            _fill.localScale = new Vector3(Mathf.Max(0.0001f, _barWidth * normalized), _barThickness, 1f);
            _fill.localPosition = new Vector3(-_barWidth * (1f - normalized) * 0.5f, 0f, 0f); // 由右往左縮
        }

        private void SetBarVisible(bool visible)
        {
            if (_fillRenderer != null) _fillRenderer.enabled = visible;
            if (_backgroundRenderer != null) _backgroundRenderer.enabled = visible;
        }
    }
}
