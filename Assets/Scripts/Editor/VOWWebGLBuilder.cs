using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Vow.Core;
using Debug = UnityEngine.Debug;

namespace Vow.EditorTools
{
    // WebGL 試玩版建置（部署到 GitHub Pages，手機開網址即可試玩）。
    // 網頁版只用於日常試玩與看畫面：瀏覽器的輸入延遲、幀率上限、觸控取樣都與原生 App 不同，不能當正式手感驗收。
    public static class VOWWebGLBuilder
    {
        public const string OutputFolder = "Builds/WebGL";
        private const string ScenePath = "Assets/Scenes/VOW_Phase1_Greybox.unity";
        private const string BuildStampToken = "__VOW_BUILD_STAMP__";

        [MenuItem("VOW/Phase 1/Build WebGL (GitHub Pages)")]
        public static void Build()
        {
            if (!File.Exists(ScenePath))
                throw new InvalidOperationException("[VOW] 找不到 " + ScenePath + "，請先執行 VOW/Phase 1/Build Greybox Scene。");

            ApplyPlayerSettings();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputFolder,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("[VOW] WebGL 建置失敗：" + summary.result + "，錯誤數 " + summary.totalErrors);

            StampIndexHtml();
            File.WriteAllText(Path.Combine(OutputFolder, ".nojekyll"), string.Empty); // 讓 Pages 原樣提供檔案，不經 Jekyll

            Debug.Log("[VOW] WebGL 建置完成：" + OutputFolder + "，" + (summary.totalSize / (1024f * 1024f)).ToString("F1") +
                      " MB，耗時 " + summary.totalTime.TotalSeconds.ToString("F0") + "s，版本 " + VowVersion.Version);
        }

        // 獨立成一步：先套用並存檔、commit 之後再建置，建置過程就不會再弄髒工作區（首頁版本列的 commit 才對得上內容）。
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "VOW";
            PlayerSettings.companyName = "VOW";
            PlayerSettings.bundleVersion = VowVersion.Version; // 首頁的 {{{ PRODUCT_VERSION }}} 由此而來

            PlayerSettings.WebGL.template = "PROJECT:VowMinimal";
            // GitHub Pages 不會替 .gz／.br 加 Content-Encoding 標頭；開啟 decompressionFallback 由載入器在瀏覽器端自行解壓
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = false; // 試玩版每次都抓最新，避免「明明部署了卻還是舊版」
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }

        // 把建置時間與 git commit 寫進首頁的版本列。
        private static void StampIndexHtml()
        {
            string indexPath = Path.Combine(OutputFolder, "index.html");
            string html = File.ReadAllText(indexPath);
            if (!html.Contains(BuildStampToken))
                throw new InvalidOperationException("[VOW] index.html 裡找不到 " + BuildStampToken + "：WebGL 範本未套用？");

            string stamp = "build " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC · " + GitShortSha();
            File.WriteAllText(indexPath, html.Replace(BuildStampToken, stamp));
        }

        private static string GitShortSha()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5000);
                    return string.IsNullOrEmpty(output) ? "no-git" : output;
                }
            }
            catch (Exception)
            {
                return "no-git";
            }
        }
    }
}
