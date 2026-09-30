# v0.14.0 深淵峽谷：Codex 接手步驟 A

日期：2026-09-30。凍結計畫 `db73474`，Claude 純邏輯實作 `c1854be`。

## 接手時的事實

- `vow-toolchain/v0140-A-mutation.log` 已跑完：190／190 CAUGHT，還原後 327 項、0 失敗；不是截圖中的 149 項中途進度。
- Codex 重跑 `verify.sh`：326 通過、1 略過、0 失敗，腳本編譯 0 錯誤，`RESULT: ALL PASS`。
- 獨立審查：A01～A22 與 K1～K18 對照凍結條文；A18 指定檔案 diff 為空，既有測試刪除 0 行，`BlockGrid` 只新增方法。
- 工作區原有 `GDD.md` 與 `docs/PLAYER_EXPERIENCE_BLUEPRINT.md` 改動保留，不納入本次提交。

## Unity 實跑發現與修補

原式與只加括號的第一版在完整 EditMode 均為 318 通過、1 失敗、8 略過。唯一失敗為 `V14A11a_AttackRange_OnlyAttackerOnCliffGetsTenPercent`：1.8 射程加成 10% 的結果少一個 float32 ULP。

Unity 自帶 Mono 探針與編譯組件 IL 確認，中間求值保留較高精度；不是 NUnit 判定或未重新編譯。修補提交 `cc51bf8`：`CanyonRules.AttackRange` 在乘積處加明確 `(float)` 收斂，再除以 100，保留原公式。玩法數值、測試、容差、凍結計畫皆未改。

修後定向 Unity：7 通過、0 失敗、1 個預定略過；完整 `verify.sh`：326 通過、1 略過、0 失敗、ALL PASS；dotnet A17：1 通過、0 失敗，10000 次呼叫的零配置門檻維持。

## 最終驗收狀態

**步驟 A 驗收通過。** 最後獨立彙核已直接讀回原始 XML、三份日誌及隔離工作樹，結論 APPROVE、無未解 finding；B／C／D 未驗、v0.14.0 未部署。

- 修後完整 EditMode：319 通過、0 失敗、8 預定略過（7 個既有 dotnet-only ＋本批 A17）。
- 修後完整 PlayMode：211／211 通過、0 失敗、0 略過（XML `v0140-A-codex-final-play.xml`，2287.4027033 秒）；Unity 已自然退出。
- 修後全 190 項突變：三個隔離工作樹分別 64／64、63／63、63／63 CAUGHT；SKIP 0、MISSED 0。各自還原後 327 項、0 失敗、退出碼 0，`RESULT: ALL MUTATIONS CAUGHT`。沒有與 Unity 同時跑。
- 190 筆原始清單依索引與完整 tuple 雜湊固定，比對三份日誌中的代號順序一致；代號重名不合併。原突變定義、測試及判定不變，外層新增新 TRX 與版本檢查，防止沿用舊結果。
- 三個工作樹為 `c1854be` 加與主專案 SHA-256 相同的射程修補；被測 production 內容等價於 `cc51bf8`。實跑後 tracked diff 僅為這份預定修補。
- A18 最終範圍複核：指定核心檔案及舊測試 diff 為空；`Assets/Tests` 僅四個新測試與其 meta，既有測試刪除 0 行；`git diff --check` 無輸出。
- 外部詳細證據：`vow-toolchain/v0140-A-floatfix-report.md`、`v0140-A-codex-review.md`、`v0140-A-codex-unity-report.md`、`v0140-A-codex-mutation-report.md`。

## 實跑指令與輸出

工作目錄為 `C:/Users/shung/.gemini/antigravity/scratch/vow`。編譯與純邏輯：

```powershell
$env:UNITY_REFS_DIR='C:/Users/shung/.gemini/antigravity/scratch/vow-toolchain/refs'
& 'C:/Program Files/Git/bin/bash.exe' Tools/DotnetCheck/verify.sh
```

實際輸出：`失敗: 0，通過: 326，略過: 1，總計: 327`、Unity 腳本 `0 個錯誤`、八項靜態掃描 PASS、`RESULT: ALL PASS`。dotnet 略過的是 Unity Mono 配置計數器前提檢查，在 Unity 已通過；Unity 略過的八項零配置案例在 dotnet 執行。

完整 Unity 回歸，無 testFilter：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\shung\.gemini\antigravity\scratch\vow' -runTests -testPlatform EditMode -testResults 'C:\Users\shung\.gemini\antigravity\scratch\vow-toolchain\v0140-A-codex-final-edit.xml' -logFile 'C:\Users\shung\.gemini\antigravity\scratch\vow-toolchain\v0140-A-codex-final-edit.log'
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\shung\.gemini\antigravity\scratch\vow' -runTests -testPlatform PlayMode -testResults 'C:\Users\shung\.gemini\antigravity\scratch\vow-toolchain\v0140-A-codex-final-play.xml' -logFile 'C:\Users\shung\.gemini\antigravity\scratch\vow-toolchain\v0140-A-codex-final-play.log'
```

XML 實際輸出：EditMode `327 total / 319 passed / 0 failed / 8 skipped`；PlayMode `211 total / 211 passed / 0 failed / 0 skipped`。

三批突變的完整啟動指令（分別執行，環境變數相同）：

```powershell
$env:PYTHONIOENCODING='utf-8'; $env:PYTHONUNBUFFERED='1'; $env:VOW_A19_START='UNITY_EXITED_ROOT_CONFIRMED'; py -3 '..\vow-toolchain\v0140-A-codex-mut-runner.py' 1 2>&1 | Tee-Object -FilePath '..\vow-toolchain\v0140-A-codex-mut-A1.log'; exit $LASTEXITCODE
$env:PYTHONIOENCODING='utf-8'; $env:PYTHONUNBUFFERED='1'; $env:VOW_A19_START='UNITY_EXITED_ROOT_CONFIRMED'; py -3 '..\vow-toolchain\v0140-A-codex-mut-runner.py' 2 2>&1 | Tee-Object -FilePath '..\vow-toolchain\v0140-A-codex-mut-A2.log'; exit $LASTEXITCODE
$env:PYTHONIOENCODING='utf-8'; $env:PYTHONUNBUFFERED='1'; $env:VOW_A19_START='UNITY_EXITED_ROOT_CONFIRMED'; py -3 '..\vow-toolchain\v0140-A-codex-mut-runner.py' 3 2>&1 | Tee-Object -FilePath '..\vow-toolchain\v0140-A-codex-mut-A3.log'; exit $LASTEXITCODE
```

實際輸出：`64 / 64`、`63 / 63`、`63 / 63`；每批 `還原後：327 個測試，0 個失敗`、`RESULT: ALL MUTATIONS CAUGHT`，退出碼皆 0。日誌及檔案雜湊在外部突變報告。

## 後續

步驟 A 通過後才能開始 B。B 的唯讀準備已寫於 `vow-toolchain/v0140-B-codex-readback.md`，未實作 Unity 地形接線、整局驗收或部署。線上版本仍為 v0.13.1。
