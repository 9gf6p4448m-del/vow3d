# 第三人稱觸控試作紀錄

2026-10-01。邏輯來源d60d656，顯示窄修0b82bac，良好起點e73e6ce；camera-lab-20261001分支、vow-camera-lab工作樹。主VOW工作樹未由本輪修改，未合併。新版尚未部署gh-pages，以免干擾Claude Code的正式版部署驗證。

## 操作與修改

- 第三人稱左下浮動搖桿連續移動、右側空白處拖曳轉視角，鏡頭朝向換算移動；俯仰10–50度，初始25度。右側短tap敵人沿用原攻擊，tap地面不導航。
- UI與Rune優先，手指開始時固定路由；雙指可同時move/look。放手、cancel、消失、失焦、切鏡頭、resize、停用與禁止英雄輸入時清狀態。
- 第三人稱收左除錯HUD且停相同region，保留CAPTURE與技能；4鏡頭工具鈕移左上，俯視維持原操作。
- `TouchGestureRouter.cs:61`、`PlayerInputService.cs`、`InputRoutingManager.cs`：沿用單一採樣，新增move/look路由與短tap區分。
- `CameraComparisonLab.cs:35`、`Phase1Bootstrap.cs`、`DuelInputRouter.cs`：鏡頭與同一proxy的原MatchGate，未另寫對局規則。
- `HeroController.cs:24`、`HeroLocomotion.cs`：新stick意圖單次接手，使用原速度/倍率/碰撞/狀態限制；敵tap交出搖桿ownership，放手不清新追擊。
- `DebugHud.cs:257`：第三人稱顯示與觸控區域同步。
- 新EditMode `ThirdPersonTouchRouterTests`、PlayMode `ThirdPersonTouchPlayTests`及meta；既有測試斷言未改。
- WebGL模板版本列標示「搖桿／滑動試作」。完整diff為來源commit d60d656。

## 實跑

工作目錄 `C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab`；證據目錄 `C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain`。

```powershell
$env:UNITY_REFS_DIR='C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/refs'
& 'C:/Program Files/Git/bin/bash.exe' Tools/DotnetCheck/verify.sh
```

最終runtime結果：333過／1既有略過／0敗，編譯0錯、8項掃描PASS、`RESULT: ALL PASS`，exit0。新純路由實際7cases，不是交接早期誤記的8；verify-final.log保留原輸出。

Unity執行檔 `C:/Users/shung/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe`；子程序先設 `$env:ALLUSERSPROFILE=$env:ProgramData`，使用Start-Process -WindowStyle Hidden。

```text
-batchmode -projectPath C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab -runTests -testPlatform EditMode -testFilter InputRoutingTests;TouchGestureRouterTests;ThirdPersonTouchRouterTests -testResults C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/touch-lab-edit-r1.xml -logFile C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/touch-lab-edit-r1.log
-batchmode -projectPath C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab -runTests -testPlatform PlayMode -testFilter Vow.Tests.PlayMode.ThirdPersonTouchPlayTests;Vow.Tests.PlayMode.CameraComparisonLabTests -testResults C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/touch-lab-play-r2.xml -logFile C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/touch-lab-play-r2.log
```

實際XML：Edit23過／0敗／0略（原16+新7），Unity exit0；Play15過／0敗／0略（原6+新9），Unity exit0。含雙指真路由、真位移/停步、目標追擊ownership、Rune生成真牆、Water真場景field、resize首幀、速度/碰撞、原MatchGate、water+rock真quicksand root與合法terrain的vent限制。

Play r1保留14過／1敗、Unity exit2，原「射程外」fixture為約4.01m但既有射程5m，tap進AttackWindup符合原規則。僅測試遠距改為公開AttackRange+2m並增加原射程判定前置斷言，原Moving／8幀追擊／距離縮短>.1m／目標斷言全部保留。r2實測log：`TouchOwnershipPrecondition distance=7.005519 attackRange=5 inRange=False`。Runtime未因此修改；獨立覆審確認未降低標準。

fresh程式覆審APPROVE，未解finding0。原搖桿/追擊ownership MEDIUM已修，真路由Play覆蓋；報告為同層toolchain/touch-review.md。`git diff --check` exit0（僅CRLF提示）。

## 畫面與送達

WebGL兩次完整成功：d60d656建置180秒；0b82bac顯示修補後387秒，`[VOW] WebGL 建置完成：Builds/WebGL，10.6 MB，耗時 387s，版本 0.13.1`，子程序exit0。新顯示僅改3行（TOP文字、搖桿底座style、footer左下位置），輸入／邏輯／測試未改，追加fresh覆審APPROVE；未因純顯示修補重跑不變的邏輯測試。

```powershell
$env:ALLUSERSPROFILE=$env:ProgramData
$env:PYTHONUTF8='1'
$p=Start-Process -FilePath 'C:/Users/shung/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe' -ArgumentList @('-batchmode','-quit','-projectPath','C:/Users/shung/.gemini/antigravity/scratch/vow-camera-lab','-buildTarget','WebGL','-executeMethod','Vow.EditorTools.VOWWebGLBuilder.Build','-logFile','C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/touch-lab-webgl-ui-build.log') -WindowStyle Hidden -PassThru
$p.WaitForExit()
```

本機預覽：http://127.0.0.1:8768/?v=0b82bac （只在這台電腦使用，預覽伺服器需保持執行）。頁面版本列 `build 2026-10-01 08:55 UTC · 0b82bac`；Chrome已確認Unity instance存在，頁面/framework/wasm/data HTTP200，未改loader為304快取。使用模擬觸控的真DOM TouchEvent驗THIRD進入、雙指move+look、release、resize640×360、TOP切回俯視；截圖已保存並重新開啟查看 `vow-toolchain/touch-lab-third-844.png`、`touch-lab-third-640.png`。第三人稱工具與技能列清楚；俯視舊大型除錯HUD在小視窗有裁切且footer可能與除錯文字重疊，未做正式HUD重設。console沒有error，但有瀏覽器WebGL INVALID_ENUM warning6次，不宣稱完全無警告。

新版未更新gh-pages，線上仍前一輪fe78256鏡頭比較。原生手機手感、效能、正式HUD與線上送達尚未驗證；整體發布驗收未完成。本機瀏覽器驗證不能代替真機。

隔離證據：本輪開始核對主工作樹為v0140-abyssal-canyon／290ee2b，結尾時該工作樹已由另一session前進到1c91bfc（16:43:05+08:00），本試作仍為camera-lab-20261001；已存在的GDD.md、PLAYER_EXPERIENCE_BLUEPRINT.md和__pycache__未碰。`git ls-remote origin refs/heads/gh-pages`仍為971386c02ac3f6574f41e3f8d4d5c9b6df1beb28（前一輪鏡頭試作）；本輪不執行正式force部署腳本，也不改Claude的部署工具。
