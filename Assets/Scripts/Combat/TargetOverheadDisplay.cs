using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 頭頂血條＋傷害飄字。全部物件在 Awake 預熱完成，戰鬥中零 Instantiate、零字串配置。
    [RequireComponent(typeof(CombatTargetBehaviour))]
    public sealed class TargetOverheadDisplay : MonoBehaviour
    {
        private const int FloatingTextPoolSize = 8;
        private const float FloatSeconds = 0.7f;
        private const float FloatRiseSpeed = 1.6f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Material _barMaterial;
        [SerializeField] private float _height = 2.4f;
        [SerializeField] private float _barWidth = 1.4f;
        [SerializeField] private float _barThickness = 0.14f;

        private readonly TextMesh[] _texts = new TextMesh[FloatingTextPoolSize];
        private readonly float[] _textTimers = new float[FloatingTextPoolSize];

        private CombatTargetBehaviour _target;
        private Transform _root;
        private Transform _fill;
        private Renderer _fillRenderer;
        private Renderer _backgroundRenderer;
        private Transform _cameraTransform;
        private MaterialPropertyBlock _block;
        private int _nextText;
        private bool _wasAlive;
        private float _lastNormalized = -1f;

        private void Awake()
        {
            _target = GetComponent<CombatTargetBehaviour>();
            _block = new MaterialPropertyBlock();

            // 刻意不掛在目標底下：石牆有非等比縮放，子物件會被拉扁；改為獨立物件、每幀跟隨位置。
            _root = new GameObject(name + "_Overhead").transform;
            _root.position = transform.position + new Vector3(0f, _height, 0f);

            CreateBarQuad("BarBackground", new Color(0.08f, 0.08f, 0.08f), 0.002f, out Transform background);
            background.localScale = new Vector3(_barWidth + 0.06f, _barThickness + 0.06f, 1f);
            CreateBarQuad("BarFill", new Color(0.9f, 0.18f, 0.16f), 0f, out _fill);
            _backgroundRenderer = background.GetComponent<Renderer>();
            _fillRenderer = _fill.GetComponent<Renderer>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < FloatingTextPoolSize; i++)
            {
                GameObject go = new GameObject("DamageText");
                go.transform.SetParent(_root, false);
                TextMesh text = go.AddComponent<TextMesh>();
                text.font = font;
                text.fontSize = 64;
                text.characterSize = 0.06f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = new Color(1f, 0.92f, 0.3f);
                if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                go.SetActive(false);
                _texts[i] = text;
            }

            _target.OnDamaged += HandleDamaged;
            _target.OnRevived += RefreshBar;
        }

        private void Start()
        {
            Camera main = Camera.main;
            if (main != null) _cameraTransform = main.transform;
            RefreshBar(); // 放在 Start：同物件上各元件的 Awake 順序不保證，此時目標的生命值才確定已初始化
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            if (_target == null) return;
            _target.OnDamaged -= HandleDamaged;
            _target.OnRevived -= RefreshBar;
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

        private void HandleDamaged(float amount)
        {
            RefreshBar();

            int slot = _nextText;
            _nextText = (_nextText + 1) % FloatingTextPoolSize;

            TextMesh text = _texts[slot];
            text.text = IntStringCache.Get(Mathf.RoundToInt(amount));
            text.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            text.gameObject.SetActive(true);
            _textTimers[slot] = FloatSeconds;
        }

        private void RefreshBar()
        {
            // 目標死亡（木樁倒下、石牆碎裂）期間不顯示血條，復活時 OnRevived 會再呼叫一次把它帶回來
            bool alive = _target.IsAlive;
            _fillRenderer.enabled = alive;
            _backgroundRenderer.enabled = alive;

            float normalized = _target.HealthNormalized;
            _fill.localScale = new Vector3(Mathf.Max(0.0001f, _barWidth * normalized), _barThickness, 1f);
            _fill.localPosition = new Vector3(-_barWidth * (1f - normalized) * 0.5f, 0f, 0f); // 由右往左縮
        }

        private void LateUpdate()
        {
            // 血條盯著「存活狀態」與「血量比例」的變化自己補畫（純浮點比較，零配置）。
            // 兩個既有缺口：① 池化的石牆重新立起來走的是 Configure，不送 OnRevived；
            // ② 友軍彈道穿透扣的是 RuneWallLogic 的血，不經 ReceiveDamage，也就不送 OnDamaged。
            bool aliveNow = _target.IsAlive;
            float normalizedNow = _target.HealthNormalized;
            if (aliveNow != _wasAlive || normalizedNow != _lastNormalized)
            {
                _wasAlive = aliveNow;
                _lastNormalized = normalizedNow;
                RefreshBar();
            }

            _root.position = transform.position + new Vector3(0f, _height, 0f);
            if (_cameraTransform != null) _root.rotation = _cameraTransform.rotation; // 永遠正對鏡頭

            float dt = Time.deltaTime;
            for (int i = 0; i < FloatingTextPoolSize; i++)
            {
                if (_textTimers[i] <= 0f) continue;

                _textTimers[i] -= dt;
                Transform textTransform = _texts[i].transform;
                textTransform.localPosition += new Vector3(0f, FloatRiseSpeed * dt, 0f);
                if (_textTimers[i] <= 0f) _texts[i].gameObject.SetActive(false);
            }
        }
    }
}
