# v0.14.0 步驟 B 證據紀錄（未驗收）

來源 snapshot：`045029dbb6e3312f3cf7d298e090faff4a891b70`，初始 B checkpoint `d97ae86353b939814860465adc60f9a5b46855f1`；A 已驗收來源 `66f98a60c0ca777fd67b29693a4d526e14055ad1`。本紀錄僅涵蓋 Unity 地形與接線；C／D 尚未開始、未部署。

## 實作位置

- `Assets/Scripts/Editor/VOWPhase1SceneBuilder.cs:217`：57 個板塊 collider、6 斜坡、4 外圍；`:362` 為44崖壁。19個整六角顯示面使用 `Assets/Settings/VOW_CanyonHex.asset`，避免三盒頂面重疊紋；原 `VOW_Phase1_NavMesh.asset` 未 rebake、與 A diff 空。
- `Assets/Scripts/Core/HeroLocomotion.cs:39`：統一地面高度與 agent 同步；`:47` 地熱飛行；`:469` 同層牆推出。`Phase1Bootstrap.cs:931` 接落地推出、`:935` 地熱 tick、`:950` 切換地形／障礙 stamp。
- `Assets/Scripts/Bootstrap/Phase1Bootstrap.cs:255`、`:279`、`:281`、`:295` 與 `Assets/Scripts/Combat/AbyssalVanguardTarget.cs:26`：五個固定 Editor 測試入口。傷害與顯形使用真扣血事件，紅藍射程對稱接地形。
- `Assets/Scripts/UI/DebugHud.cs:255`：真峽谷局收起六個除錯觸控區域；平地維持原有 consume／MatchGate。`Assets/Tests/PlayMode/EditorGameViewSetUpFixture.cs:19`：Editor 全域真 GameView 640×480，B13 暫用844×390、結束恢復。
- `Assets/Tests/PlayMode/CanyonPlayTests.cs:83` 與 `CanyonVisibilityPlayTests.cs:109`：32 個固定方法，含逐幀高度／跨崖、真施法、地熱、射程、顯形及258點遮擋對照。八個既有 fixture 僅 SetUp 各加一行 `UseFlatCaptureSpecForTest`，原斷言零刪改。

## 原文命令及實際輸出

下列命令在主專案執行；外部工具、原始 log／XML 位於相鄰 `../vow-toolchain/`。

```powershell
$env:UNITY_REFS_DIR='C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/refs'
& 'C:/Program Files/Git/bin/bash.exe' Tools/DotnetCheck/verify.sh
& 'C:/Program Files/Git/bin/bash.exe' ../vow-toolchain/v090b_scene.sh v0140-B-codex-scene-r2
& 'C:/Program Files/Git/bin/bash.exe' ../vow-toolchain/run_unity.sh v0140-B-codex-full-edit EditMode
& 'C:/Program Files/Git/bin/bash.exe' ../vow-toolchain/run_unity.sh v0140-B-codex-full-play-r2 PlayMode
```

|原始證據|實際輸出|
|---|---|
|`v0140-B-root-verify-11.log`|純邏輯326通過／1略過／0失敗；Unity腳本編譯0錯；靜態掃描PASS；`RESULT: ALL PASS`|
|`v0140-B-codex-scene-r2.log`|場景建置 Unity return code 0；原NavMesh diff空|
|`v0140-B-codex-full-edit.xml`|327 total／319 passed／0 failed／8 skipped|
|`v0140-B-codex-full-play-r2.xml`|243 total／243 passed／0 failed／0 skipped；2326.1234829秒，70次原狂怒重播|
|`v0140-B-codex-wiring-r8.xml`|32／32；B13真844×390、258點、minimum 3/15、buried 0/15|

EditMode八個略過均為既有 Mono 配置量測限制：CanyonZeroAlloc、Capture19ZeroAlloc、CaptureSanctuaryScoring、CaptureZeroAlloc 各一個及 GridNavigator 四個。它們未在 Unity EditMode 驗證，不能計為Unity通過；對應 dotnet 配置量測保留。既有 wrapper 最後 grep 無失敗時會回1，判讀使用原始 XML 與 Unity 真終態。

B13 保存真 GameView RenderTexture 像素，外部 valley／cliff RGBA經 System.Drawing 轉PNG；主代理與獨立 reviewer已查看844×390畫面。此證據不代替原生手機手感驗收。

## 保留的失敗歷程

首輪完整 PlayMode 為224／243：18個舊案例的真640×480前提失敗、1個WATER觸控失敗。修正Editor環境後 smoke-r1為33／34；平地除錯區域恢復consume後 smoke-r2為34／34，完整r2為243／243。舊 XML 不覆寫，未改既有案例斷言或窗口。

新隔離 Library 的 UPM 兩次初始化失敗皆未產生突變驗證；fresh Astra定位到子process缺 `ALLUSERSPROFILE`，UPM對該值直接path.join而失敗。僅執行process補既有ProgramData後正常載入23packages，未改Packages／source／系統環境。詳 `v0140-B-upm-diagnosis.md`。

## 待驗收事項

- **B12待使用者裁定**：凍結計畫`:293`、`:422` 同時要求牆幾何中心在地面與2m牆下緣貼地，互相矛盾。目前source／tests採下緣貼地：谷底下緣−1、中心0；已提出具體兩選項。未回覆前不改計畫、不宣告B12字面符合。
- 原21群突變／32方法不能縮減；G18的兩個方法因B12裁定待補。原G11在120幀前搖assert紅，未取得dry指定的位移紅，原記錄永久保留NOT_CAUGHT；新增G11b補充突變只在正式Stop前多走0.1m，不改健康來源或測試。實跑先通前搖assert，再於原`:423` 位移assert紅（預期≤0.05、實際0.100000106），還原32／32綠。不能將G11b冒稱原G11實跑結果。
- 原始 journals 的 `PENDING`、`RUNNING` 或 `PARTIAL` 不當作全批完成；單群採納須直接讀fault／restore XML、真行為assert及完整550檔hash還原。最新外部索引 `v0140-B-root-progress.md`。

使用者原有 `GDD.md` 與 `docs/PLAYER_EXPERIENCE_BLUEPRINT.md` 未納入本工作。B仍未驗收，整版未驗證、未部署；待事項全部滿足才更新本紀錄為驗收終態。

## 隔離突變終態證據（fresh彙核通過，B仍待裁）

|批次／唯一session|已跑範圍|健康／還原|
|---|---|---|
|B1 `14363696a14344a7a4d9966c89484c94`|G01–G06，14個故障方法|baseline32綠、6次各還原32綠|
|B2 `d4db005d1d494137a2e28d81e8cb3923`|G07–G12；G11保留NOT_CAUGHT，其他5群5方法已採納|baseline32綠、每群還原32綠；G12原restore XML採單群證據，journal保持RUNNING|
|B2 `e11ec65f784f4fd497714071b1359018`|G13視野renderer一致性，`:434` 真行為紅|baseline32綠、還原32綠，runner exit0、PARTIAL|
|B2 `a5d860b6ada64462a0b6e59c2f2b7cc8`|G11b補充位移鑑別力，`:423` 紅|baseline32綠、還原32綠，runner exit0、PARTIAL|
|B3 `516bf98b2f544b959a8ecfbd3afd4244`|G14–G17、G19–G21，9個故障方法|baseline32綠、7次各還原32綠，runner exit0、PARTIAL；G18待裁|

原始資料夾皆為外部 `v0140-B-mutation-<批次>-<session>/`；fault／restore XML與UTC／GUID不覆寫。fresh獨立行為審查已直接核B3九case、G13與G11b；累計47份原XML／863 case，audit errors=[]，採納原19群29方法CAUGHT及G11b補充CAUGHT。原19群29方法加G11b一方法，共30個方法已有目標故障紅／健康還原綠證據；G18兩方法未跑。不得宣稱原21群全CAUGHT、0 miss或完整B通過。

G13與G11b使用經fresh靜態覆審的單群scheduler（SHA256 `B930EE540824ED4FD88E65B46633DA9899197D352B534E54D254944A6F08D648`）。G13用原definitions（21群／32方法），G11b用保留原21群加一補充群的definitions（SHA256 `642F89CA8A0BBA8F5CBF0274CAD8286E695776400037ABF98DD769476751F163`，22群／32 distinct方法）。baseline／restore始終固定全部32。

兩次實跑的原文呼叫如下（執行前保存原環境、僅process補 `$env:ALLUSERSPROFILE=$env:ProgramData`，finally恢復原值；未改系統環境）：

```powershell
$taskTools='C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain'
& powershell -NoProfile -File (Join-Path $taskTools 'v0140-B-mutation-selected-runner.ps1') -Execute -GroupId G13 -BatchId B2 -WorktreePath C:/Users/shung/.gemini/antigravity/scratch/v0140-B-isolated/B2/vow-mut-B2 -SnapshotSha 045029dbb6e3312f3cf7d298e090faff4a891b70 -SnapshotManifestPath (Join-Path $taskTools 'v0140-B-mutation-snapshot-B2.json') -ReviewedDefinitionsSha256 0D00E7FA6CA06A80BF37B3D6204D47F23028C352ADFD5B32E15E7AAD410B4AEA -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe'
& powershell -NoProfile -File (Join-Path $taskTools 'v0140-B-mutation-selected-runner.ps1') -Execute -GroupId G11b -BatchId B2 -DefinitionsPath (Join-Path $taskTools 'v0140-B-mutation-G11b-definitions.json') -WorktreePath C:/Users/shung/.gemini/antigravity/scratch/v0140-B-isolated/B2/vow-mut-B2 -SnapshotSha 045029dbb6e3312f3cf7d298e090faff4a891b70 -SnapshotManifestPath (Join-Path $taskTools 'v0140-B-mutation-snapshot-B2.json') -ReviewedDefinitionsSha256 642F89CA8A0BBA8F5CBF0274CAD8286E695776400037ABF98DD769476751F163 -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe'
```

末次B2核對原文輸出：`shaMismatch=0 inventoryDiff=0 statusCount=0 files=550 head=045029dbb6e3312f3cf7d298e090faff4a891b70`；B1／B3亦各550檔完整還原、clean。外部 `v0140-B-mutation-behavior-review.md`、`v0140-B-mutation-selected-review.md`、`v0140-B-mutation-B3-045029d-execution-report.md` 及 `v0140-B-main-final-xml-readback.json` 保存逐方法行號、原始命令、XML SHA與採納界線。
