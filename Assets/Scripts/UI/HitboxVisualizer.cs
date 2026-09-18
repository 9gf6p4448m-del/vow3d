using UnityEngine;
using UnityEngine.Rendering;
using Vow.Core;

namespace Vow.UI
{
    // Day 1 調試工具：一鍵把場上所有 Primitive Collider 以線框畫出來（Box／Capsule／Sphere）。
    // 用 GL 立即模式在鏡頭渲染結束時疊畫；頂點全部走預先算好的單位圓表，繪製期間零配置。
    public sealed class HitboxVisualizer : MonoBehaviour
    {
        private const int CircleSegments = 24;
        private const int MaxColliders = 64;

        private static readonly float[] UnitCos = BuildTable(true);
        private static readonly float[] UnitSin = BuildTable(false);

        [SerializeField] private Material _lineMaterial; // 由 SceneBuilder 指派的材質資產（裝置版才不會被剔除）
        [SerializeField] private Color _heroColor = new Color(0.2f, 1f, 0.35f);
        [SerializeField] private Color _otherColor = new Color(1f, 0.55f, 0.1f);

        private readonly Collider[] _colliders = new Collider[MaxColliders];
        private readonly bool[] _isHero = new bool[MaxColliders];
        private int _colliderCount;
        private Material _runtimeMaterial;
        private bool _visible;

        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (_visible) RefreshColliders(); // 只在打開的那一刻掃場景（FindObjectsOfType 會配置，不可放進每幀路徑）
            }
        }

        private void Awake()
        {
            if (_lineMaterial != null) return;

            // 後備：手動拼場景、沒有指派材質資產時才走 Shader.Find（Editor 內可用；裝置版可能被剔除）
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("[VOW] HitboxVisualizer 沒有線框材質，Hitbox 顯示停用。請執行 VOW/Phase 1/Build Greybox Scene。", this);
                return;
            }

            _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _runtimeMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _runtimeMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _runtimeMaterial.SetInt("_Cull", (int)CullMode.Off);
            _runtimeMaterial.SetInt("_ZWrite", 0);
            _runtimeMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            _lineMaterial = _runtimeMaterial;
        }

        private void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        }

        public void RefreshColliders()
        {
            _colliderCount = 0;
            Collider[] found = FindObjectsOfType<Collider>();
            for (int i = 0; i < found.Length && _colliderCount < MaxColliders; i++)
            {
                Collider c = found[i];
                if (c.isTrigger) continue;
                if (!(c is BoxCollider) && !(c is CapsuleCollider) && !(c is SphereCollider)) continue;
                if (c.transform.localScale.x > 20f) continue; // 地板不畫

                _colliders[_colliderCount] = c;
                _isHero[_colliderCount] = c.GetComponentInParent<HeroController>() != null;
                _colliderCount++;
            }
        }

        // 兩條渲染路徑都接、都畫：OnRenderObject 與 SRP 的 endCameraRendering。
        // 刻意不做同幀去重——去重會變成「先到先得」：若先到的那條路徑在當前管線下其實畫不出來，反而會擋掉真的能畫的那條。
        // 兩條都有效時同一組線被畫兩次，視覺上沒有差別，成本可忽略（調試工具）。
        private void OnRenderObject()
        {
            Camera current = Camera.current;
            if (current != null && current.cameraType == CameraType.Game) Draw();
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (renderedCamera.cameraType == CameraType.Game) Draw();
        }

        private void Draw()
        {
            if (!_visible || _lineMaterial == null || _colliderCount == 0) return;

            _lineMaterial.SetPass(0);
            for (int i = 0; i < _colliderCount; i++)
            {
                Collider c = _colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;

                Color color = _isHero[i] ? _heroColor : _otherColor;
                if (c is BoxCollider box) DrawBox(box, color);
                else if (c is CapsuleCollider capsule) DrawCapsule(capsule, color);
                else if (c is SphereCollider sphere) DrawSphere(sphere, color);
            }
        }

        private static void DrawBox(BoxCollider box, Color color)
        {
            GL.PushMatrix();
            GL.MultMatrix(box.transform.localToWorldMatrix);
            GL.Begin(GL.LINES);
            GL.Color(color);

            Vector3 c = box.center;
            Vector3 e = box.size * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int t = -1; t <= 1; t += 2)
                {
                    Line(c + new Vector3(-e.x, s * e.y, t * e.z), c + new Vector3(e.x, s * e.y, t * e.z));
                    Line(c + new Vector3(s * e.x, -e.y, t * e.z), c + new Vector3(s * e.x, e.y, t * e.z));
                    Line(c + new Vector3(s * e.x, t * e.y, -e.z), c + new Vector3(s * e.x, t * e.y, e.z));
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        // 以世界座標繪製（Collider 的半徑在非等比縮放下取水平最大軸，與 PhysX 行為一致）。僅支援 Y 軸向膠囊。
        private static void DrawCapsule(CapsuleCollider capsule, Color color)
        {
            Transform t = capsule.transform;
            Vector3 scale = t.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(capsule.height * Mathf.Abs(scale.y), radius * 2f);
            float half = height * 0.5f - radius;

            Vector3 center = t.TransformPoint(capsule.center);
            Vector3 up = t.up;
            Vector3 right = t.right;
            Vector3 forward = t.forward;
            Vector3 top = center + up * half;
            Vector3 bottom = center - up * half;

            GL.Begin(GL.LINES);
            GL.Color(color);
            Circle(top, right, forward, radius, 0, CircleSegments);
            Circle(bottom, right, forward, radius, 0, CircleSegments);
            Circle(top, right, up, radius, 0, CircleSegments / 2);                   // 上半球兩道弧
            Circle(top, forward, up, radius, 0, CircleSegments / 2);
            Circle(bottom, right, up, radius, CircleSegments / 2, CircleSegments);   // 下半球兩道弧
            Circle(bottom, forward, up, radius, CircleSegments / 2, CircleSegments);
            Line(top + right * radius, bottom + right * radius);
            Line(top - right * radius, bottom - right * radius);
            Line(top + forward * radius, bottom + forward * radius);
            Line(top - forward * radius, bottom - forward * radius);
            GL.End();
        }

        private static void DrawSphere(SphereCollider sphere, Color color)
        {
            Transform t = sphere.transform;
            Vector3 scale = t.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = t.TransformPoint(sphere.center);

            GL.Begin(GL.LINES);
            GL.Color(color);
            Circle(center, Vector3.right, Vector3.forward, radius, 0, CircleSegments);
            Circle(center, Vector3.right, Vector3.up, radius, 0, CircleSegments);
            Circle(center, Vector3.forward, Vector3.up, radius, 0, CircleSegments);
            GL.End();
        }

        private static void Circle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int fromSegment, int toSegment)
        {
            for (int i = fromSegment; i < toSegment; i++)
            {
                int next = (i + 1) % CircleSegments;
                Vector3 a = center + (axisA * UnitCos[i] + axisB * UnitSin[i]) * radius;
                Vector3 b = center + (axisA * UnitCos[next] + axisB * UnitSin[next]) * radius;
                Line(a, b);
            }
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private static float[] BuildTable(bool cosine)
        {
            float[] table = new float[CircleSegments];
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                table[i] = cosine ? Mathf.Cos(angle) : Mathf.Sin(angle);
            }
            return table;
        }
    }
}
