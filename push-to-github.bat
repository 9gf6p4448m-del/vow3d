@echo off
cd /d "%~dp0"
echo ============================================
echo   VOW 3D - Build and Push to GitHub Pages
echo ============================================
echo.

echo [1/3] Building Web 3D PWA...
cd web
call npm run build
if %errorlevel% neq 0 (
    echo [ERROR] Web build failed!
    pause
    exit /b 1
)

echo.
echo [2/3] Publishing to GitHub Pages (gh-pages branch)...
call npx gh-pages -d dist -r https://github.com/9gf6p4448m-del/vow3d.git --dotfiles
if %errorlevel% neq 0 (
    echo [WARNING] gh-pages push had issues, continuing with main branch...
)
cd ..

echo.
echo [3/3] Committing and pushing main branch...
git add .
git commit -m "update: sync vow3d project updates"
git push origin main
if %errorlevel% neq 0 (
    echo [ERROR] Git push failed!
    pause
    exit /b 1
)

echo.
echo ============================================
echo   SUCCESS! VOW 3D is deployed!
echo   Online URL: https://9gf6p4448m-del.github.io/vow3d/
echo ============================================
echo.
pause
exit /b 0
