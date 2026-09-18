using UnityEngine;

namespace Vow.Core
{
    // 執行期建立平面 Quad 的唯一入口（血條、閃白、貼花）。
    //
    // 不用 GameObject.CreatePrimitive(PrimitiveType.Quad)：它會附帶 MeshCollider，
    // ① ARCHITECTURE §壹 規定碰撞器一律 Primitive、禁用 MeshCollider；
    // ② IL2CPP 建置（WebGL／iOS／Android）會把專案裡沒有任何引用的 MeshCollider 類別剔除，
    //    執行期 CreatePrimitive 每建一個 Quad 就噴一次「Can't add component because class 'MeshCollider' doesn't exist」。
    public static class QuadMeshFactory
    {
        private static Mesh _sharedQuad;

        // 1x1、中心在原點、面向 -Z（與 Unity 內建 Quad 相同），全場共用同一份 Mesh。
        public static Mesh SharedQuad
        {
            get
            {
                if (_sharedQuad != null) return _sharedQuad;

                _sharedQuad = new Mesh { name = "VOW_SharedQuad" };
                _sharedQuad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
                };
                _sharedQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                _sharedQuad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                _sharedQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                _sharedQuad.RecalculateBounds();
                return _sharedQuad;
            }
        }

        // 只在預熱階段（Awake）呼叫；戰鬥中不得建立物件。
        public static GameObject Create(string objectName, Material material)
        {
            GameObject quad = new GameObject(objectName);
            quad.layer = 2; // Ignore Raycast

            quad.AddComponent<MeshFilter>().sharedMesh = SharedQuad;
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }
    }
}
