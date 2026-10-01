# 鏡頭試作交付紀錄

日期：2026-10-01；本次交付是WebGL視角比較試作，非完整新玩法或原生手機驗收。

## 修改

- `Assets/Scripts/Bootstrap/CameraComparisonLab.cs:12`：獨立鏡頭元件、視角切換、防牆縮近、沿用原觸控路由。
- `Assets/Scripts/Input/TouchGestureRouter.cs`、`PlayerInputService.cs`：有效UI tap的release座標。
- `Assets/Tests/PlayMode/CameraComparisonLabTests.cs:16`：6項鏡頭／輸入／版面測試。
- `Assets/WebGLTemplates/VowMinimal/index.html:33`：獨立試作與建置來源標記。
- `docs/DESIGN_BLUEPRINT_20261001.md:1`：整理討論決議，標明待定與未實作事項。

## 實跑指令與輸出

工作目錄為 `C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab`。

```powershell
$env:UNITY_REFS_DIR='C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/refs'
& 'C:/Program Files/Git/bin/bash.exe' Tools/DotnetCheck/verify.sh
```

實際輸出：326過／1既有略過／0敗，Unity腳本編譯0錯，8項靜態掃描PASS，`RESULT: ALL PASS`。本項有既有略過，不代表全套無略過。

Unity執行檔：`C:/Users/shung/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe`；啟動子程序皆先設 `$env:ALLUSERSPROFILE=$env:ProgramData`。

```text
-batchmode -projectPath C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab -runTests -testPlatform EditMode -testFilter InputRoutingTests;TouchGestureRouterTests -testResults C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/camera-lab-input-edit.xml
-batchmode -projectPath C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab -runTests -testPlatform PlayMode -testFilter Vow.Tests.PlayMode.CameraComparisonLabTests -testResults C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/camera-lab-play-r3.xml
```

實際XML：EditMode `total=16 passed=16 failed=0 skipped=0`；PlayMode `total=6 passed=6 failed=0 skipped=0`；兩者Unity exit0。前兩輪失敗log保留，測試修正理由見 `CAMERA_COMPARISON_LAB.md`。獨立程式覆審 APPROVE，無未解finding。

```powershell
$env:ALLUSERSPROFILE=$env:ProgramData
$env:PYTHONUTF8='1'
$p=Start-Process -FilePath 'C:/Users/shung/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe' -ArgumentList @('-batchmode','-quit','-projectPath','C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab','-buildTarget','WebGL','-executeMethod','Vow.EditorTools.VOWWebGLBuilder.Build','-logFile','C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/camera-lab-webgl-utf8-build.log') -WindowStyle Hidden -PassThru
$p.WaitForExit()
```

實際輸出：`Unity WebGL exit=0`；log第612行：`[VOW] WebGL 建置完成：Builds/WebGL，10.6 MB，耗時 279s，版本 0.13.1`。首次link因Python cp950解碼UTF-8輸出失敗；只在建置子程序啟用PYTHONUTF8後重現成功，再跑上述完整builder。版本0.13.1是試作的已驗收基底，非正式v0.14驗收。

## 送達

僅在Pages原root旁新增camera-lab目錄；未使用force push。

```text
git push origin HEAD:gh-pages
git log origin/gh-pages -1 --format='%H %cI %s'
```

實際输出：`e82603c..971386c HEAD -> gh-pages`；提交 `971386c02ac3f6574f41e3f8d4d5c9b6df1beb28`，時間 `2026-10-01T15:54:30+08:00`。

試玩：https://9gf6p4448m-del.github.io/vow3d/camera-lab/?v=fe78256

Chrome線上實測：頁面與loader/framework/wasm/data皆HTTP200，`window.vowUnityInstance`存在。可視畫面確認TOP→THIRD、LEFT、RIGHT、LEFT→RESET、TOP DOWN；頁面版本列 `build 2026-10-01 07:52 UTC · fe78256`。favicon.ico為404，另有瀏覽器WebGL INVALID_ENUM警告，遊戲仍成功運行，不宣稱console完全無警告。

截圖已保存並重新開啟查看：`C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/camera-lab-top-online.png`、`camera-lab-third-online.png`、`camera-lab-640-local.png`。前兩者為線上截圖。CSS844×390及640×360、桌面DPR1.25；HUD顯示幀率為當下桌面數字，未作手機效能量測。

## 試玩範圍與限制

手機先橫放；THIRD進第三人稱、TOP DOWN回俯視，LEFT／RIGHT轉向、RESET重置。點地移動、點敵普攻沿用舊規則。滑步、依武器攻擊與新牆管理尚未實作。

640窄画面有部分按鈕文字擁擠；除錯面板遮住角色部分身體，仍需正式HUD設計。原生手機觸控舒適度、第一人稱比較、地熱飛行與震屏後距牆仍未驗證。若顯示舊版，關閉試作分頁重新開啟帶版本參數的網址；本次模板使用原有sw自我解除註冊及dataCaching=false。
