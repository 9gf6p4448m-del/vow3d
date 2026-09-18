namespace Vow.Core
{
    // 版本字串的單一事實來源。遊戲內 HUD、WebGL 首頁、PlayerSettings.bundleVersion 全部從這裡取值——
    // 每次部署前改這一個常數，就能從首頁那一行判斷「手機上看到的是不是剛推上去的那一版」。
    public static class VowVersion
    {
        public const string Version = "0.2.0";
        public const string Label = "VOW Phase 1 Greybox v" + Version;
    }
}
