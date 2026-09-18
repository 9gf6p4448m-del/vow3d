using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if VOW_HAS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Vow.EditorTools
{
    // 灰盒所需的全部資產（URP 管線資產、材質、棋盤格貼圖）一律由程式產生並存成 .asset，
    // 運行期只透過序列化引用取用——裝置版不依賴 Shader.Find（沒被任何材質引用的 Shader 會被打包剔除）。
    internal static class GreyboxAssetFactory
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string MaterialsFolder = "Assets/Settings/Materials";

        private const string UrpLit = "Universal Render Pipeline/Lit";
        private const string UrpUnlit = "Universal Render Pipeline/Unlit";

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;

            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ───────────────────────── URP ─────────────────────────

        // 專案若尚未指定渲染管線，建立 URP 資產（Forward+）並設為預設。已有設定則不動。
        public static void EnsureRenderPipeline()
        {
#if VOW_HAS_URP
            if (GraphicsSettings.defaultRenderPipeline != null) return;

            EnsureFolder(SettingsFolder);
            string rendererPath = SettingsFolder + "/VOW_UniversalRenderer.asset";
            string pipelinePath = SettingsFolder + "/VOW_URP.asset";

            // 順序比照 URP 自己的 CreateRendererAsset：先 CreateAsset，再 ReloadAllNullIn 補齊內建資源引用
            UniversalRendererData rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
            ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
            rendererData.renderingMode = RenderingMode.ForwardPlus; // ARCHITECTURE §壹：啟用 Forward+
            EditorUtility.SetDirty(rendererData);

            UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            Debug.Log("[VOW] 已建立並啟用 URP 管線資產（Forward+）：" + pipelinePath);
#else
            Debug.LogWarning("[VOW] 未偵測到 com.unity.render-pipelines.universal，略過 URP 管線資產建立。");
#endif
        }

        // ───────────────────────── 貼圖 ─────────────────────────

        // 2x2 棋盤格，地板以 tiling 鋪成每格 1 公尺——1.4m／0.9m／0.5m 的滑步距離可以直接用地磚目測。
        public static Texture2D EnsureCheckerTexture()
        {
            string path = SettingsFolder + "/VOW_Checker.asset";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "VOW_Checker",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            Color32 light = new Color32(150, 150, 150, 255);
            Color32 dark = new Color32(105, 105, 105, 255);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool even = (x < size / 2) == (y < size / 2);
                    pixels[y * size + x] = even ? light : dark;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);

            EnsureFolder(SettingsFolder);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        // ───────────────────────── 材質 ─────────────────────────

        public static Material EnsureLitMaterial(string name, Color color, Texture2D texture = null, float tiling = 1f)
        {
            Material material = LoadOrCreate(name, UrpLit, "Standard");
            SetColor(material, color);
            if (texture != null)
            {
                SetTexture(material, texture, tiling);
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.05f);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Material EnsureUnlitMaterial(string name, Color color, bool transparent, bool doubleSided)
        {
            Material material = LoadOrCreate(name, UrpUnlit, transparent ? "Sprites/Default" : "Unlit/Color");
            SetColor(material, color);

            if (transparent && material.HasProperty("_Surface"))
            {
                // URP 材質的「透明」不是一個開關，而是這一整組屬性＋關鍵字＋渲染佇列
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            if (doubleSided && material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);

            EditorUtility.SetDirty(material);
            return material;
        }

        // 會讀頂點色的透明材質：LineRenderer 的 startColor/endColor 是寫進頂點色的，URP Lit／Unlit 都不讀頂點色，
        // 用它們的話預警箭頭的顏色與 Snap 高光會完全失效。Sprites/Default 讀頂點色、透明混合、雙面，在 URP 下以 SRPDefaultUnlit 繪製。
        public static Material EnsureVertexColorMaterial(string name)
        {
            Material material = LoadOrCreate(name, "Sprites/Default", "Legacy Shaders/Particles/Alpha Blended");
            EditorUtility.SetDirty(material);
            return material;
        }

        // GL 線框用。Hidden/* Shader 沒被任何資產引用時會在裝置版被剔除；存成材質資產、由場景序列化引用，它才會被打包。
        public static Material EnsureGlLineMaterial(string name)
        {
            Material material = LoadOrCreate(name, "Hidden/Internal-Colored", "Sprites/Default");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always); // 線框永遠可見，不被模型遮住
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreate(string name, string preferredShader, string fallbackShader)
        {
            EnsureFolder(MaterialsFolder);
            string path = MaterialsFolder + "/" + name + ".mat";

            Shader shader = Shader.Find(preferredShader);
            if (shader == null) shader = Shader.Find(fallbackShader);
            if (shader == null)
                throw new System.InvalidOperationException("[VOW] 找不到 Shader：" + preferredShader + " 與後備 " + fallbackShader);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void SetTexture(Material material, Texture2D texture, float tiling)
        {
            string property = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            material.SetTexture(property, texture);
            material.SetTextureScale(property, new Vector2(tiling, tiling));
        }
    }
}
