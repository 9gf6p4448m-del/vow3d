using UnityEngine;
using Vow.Core.Logic;

namespace Vow.Combat.Feedback
{
    // 反應成立時的飄字（V061_FEEDBACK_PLAN.md §1）。Awake 內把整個池建好（new GameObject +
    // AddComponent<TextMesh> + LegacyRuntime.ttf，不用 CreatePrimitive、不用 Shader.Find）。
    //
    // 池的設計：只有六種可能的訊息（五個反應＋ROOTED），所以六個 TextMesh 各自固定顯示其中一種、
    // .text 只在 Initialize 設一次、執行期永遠不再改字。
    // 實測踩到的真正地雷（F6 除錯記錄）：不是 `.text =` 設值本身，是讀 `TextMesh.text` 這個
    // **getter**——Unity 這份 legacy TextMesh 的 .text getter 每次讀都配置一份新字串，讀幾次
    // 就配置幾次。Show() 一律用 `_labels[labelIndex]`（自己快取的陣列）取代 `text.text` 讀值，
    // 一次都不讀那個 getter。標籤固定一個物件一個，是為了把 .text 賦值（比較次要但同樣可疑，
    // 未獨立排除）的機會降到最低——賦值只在 Initialize 發生一次，Show() 之後只剩位置／轉向／
    // 啟用狀態在變，這幾項都沿用 TargetOverheadDisplay 已經驗證過零配置的手法。
    public sealed class ReactionCalloutDisplay : MonoBehaviour
    {
        private const int LabelCount = 6;
        private const float CalloutSeconds = 1.2f;
        private const float RiseSpeed = 0.5f;
        private const float HeightOffset = 2.6f; // 離地 ≥2.5m：不被場上的石牆／符印牆擋住
        private const float CharacterSize = 0.14f; // 比傷害飄字（0.06）明顯大

        // 索引對齊 ElementCalloutLogic：0 QUICKSAND、1 STEAM、2 FIRESTORM、3 BOIL、4 RESCUE、5 ROOTED。
        private static readonly Color[] LabelColors =
        {
            new Color(0.82f, 0.68f, 0.35f), // 流沙：黃褐
            new Color(0.18f, 0.27f, 0.34f), // 蒸氣：深藍灰，與白霧區保持對比
            new Color(1.00f, 0.45f, 0.15f), // 火浪：橙
            new Color(1.00f, 0.25f, 0.20f), // 爆沸：紅
            new Color(0.30f, 0.85f, 0.40f), // 救援：綠
            new Color(0.95f, 0.85f, 0.25f), // ROOTED：黃
        };

        private readonly TextMesh[] _texts = new TextMesh[LabelCount];
        private readonly float[] _timers = new float[LabelCount];
        private Transform _cameraTransform;
        private string[] _labels;

        public int ShowCount { get; private set; }
        public string LastLabel { get; private set; }
        public Vector3 LastWorldPosition { get; private set; }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < LabelCount; i++)
                    if (_texts[i] != null && _texts[i].gameObject.activeSelf) n++;
                return n;
            }
        }

        private void Awake()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < LabelCount; i++)
            {
                GameObject go = new GameObject("ReactionCallout");
                go.transform.SetParent(transform, false);
                TextMesh text = go.AddComponent<TextMesh>();
                text.font = font;
                text.fontSize = 64;
                text.characterSize = CharacterSize;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = LabelColors[i];
                if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                go.SetActive(false);
                _texts[i] = text;
            }
        }

        private void Start()
        {
            Camera main = Camera.main;
            if (main != null) _cameraTransform = main.transform;
        }

        // 標籤表在初始化時建好一次，直接寫進各自固定的 TextMesh（BOIL 的傷害數字讀 tuning，
        // Awake 時還沒注入，所以標籤表要等這裡才建得出來）。冪等：重複呼叫不重建、不重寫。
        public void Initialize(ElementTuning tuning)
        {
            if (_labels != null) return;
            float boilDamage = tuning != null ? tuning.BoilDamage : 0f;
            _labels = new[]
            {
                "QUICKSAND", "STEAM", "FIRESTORM",
                "BOIL " + IntStringCache.Get(Mathf.RoundToInt(boilDamage)),
                "RESCUE", "ROOTED",
            };
            for (int i = 0; i < LabelCount; i++)
                if (_texts[i] != null) _texts[i].text = _labels[i];
        }

        // worldPosition：地面座標（Y 由這裡加上離地高度）；labelIndex 對齊 ElementCalloutLogic 的常數，
        // 直接指到那個標籤專屬的 TextMesh——執行期不再改 .text，只搬位置／轉向／啟用狀態。
        public void Show(Vector3 worldPosition, int labelIndex)
        {
            if (_labels == null || labelIndex < 0 || labelIndex >= LabelCount) return;

            TextMesh text = _texts[labelIndex];

            Vector3 position = worldPosition;
            position.y = worldPosition.y + HeightOffset;
            text.transform.position = position;
            if (_cameraTransform != null) text.transform.rotation = _cameraTransform.rotation;
            text.gameObject.SetActive(true);

            _timers[labelIndex] = CalloutSeconds;

            ShowCount++;
            LastLabel = _labels[labelIndex];
            LastWorldPosition = position;
        }

        private void LateUpdate()
        {
            bool haveCamera = _cameraTransform != null;
            Quaternion rotation = haveCamera ? _cameraTransform.rotation : default;
            float dt = Time.deltaTime;

            for (int i = 0; i < LabelCount; i++)
            {
                if (_timers[i] <= 0f) continue;

                _timers[i] -= dt;
                Transform t = _texts[i].transform;
                t.position += new Vector3(0f, RiseSpeed * dt, 0f);
                if (haveCamera) t.rotation = rotation; // 永遠正對鏡頭

                if (_timers[i] <= 0f) _texts[i].gameObject.SetActive(false);
            }
        }
    }
}
