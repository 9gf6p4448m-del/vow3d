@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ============================================
echo   VOW 誓約 - 一鍵打包推送至 GitHub Pages
echo ============================================
echo.
echo [1/4] 正在編譯 3D PWA Web 端...
cd web
call npm run build
if %errorlevel% neq 0 (
    echo.
    echo ❌ 編譯失敗，請檢查代碼！
    pause
    exit /b 1
)
cd ..

echo.
echo [2/4] 正在發布至 gh-pages 分支（手機網址）...
cd web
call npx gh-pages -d dist -r https://github.com/9gf6p4448m-del/vow3d.git
if %errorlevel% neq 0 (
    echo ⚠️ gh-pages 推送異常，將繼續推送 main 分支...
)
cd ..

echo.
echo [3/4] 正在提交並推送 main 分支代碼庫...
git add .
git commit -m "update: release new version of VOW"
git push origin main
if %errorlevel% neq 0 (
    echo.
    echo ❌ Git 推送失敗，請確認網路或登入狀態！
    pause
    exit /b 1
)

echo.
echo ============================================
echo   ✅ 發布成功！
echo   手機線上網址: https://9gf6p4448m-del.github.io/vow3d/
echo   手機點擊重整或從主畫面點開即可體驗最新版本！
echo ============================================
echo.
pause
exit /b 0
