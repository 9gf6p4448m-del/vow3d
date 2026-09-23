# VOW 誓約 — Codex 接手紀錄（2026-09-23）

## 目前狀態

- 專案：Unity 2022.3.62f1、C#、URP。工程約束見 `ARCHITECTURE.md`，詳細驗收見 `docs/PHASE1_ACCEPTANCE_GUIDE.md`。
- `main` 為 `e6e9d30`，與 `origin/main` 同步；接手時工作樹乾淨。`e6e9d30` 記錄 v0.6.1 驗收與線上試玩結果。
- v0.6.1 新增五種元素反應飄字、英雄受困 `ROOTED` 飄字與 HUD `ROOTED`／`SLOWED`。規格與凍結驗收見 `docs/V061_FEEDBACK_PLAN.md`；結果見驗收指南 §15。
- 驗收指南記錄：`verify.sh` 195 通過、1 略過；Unity EditMode 0 紅、PlayMode 99 全綠；突變 114／114 CAUGHT。這是 2026-09-21 的既有紀錄，本次接手未重跑完整測試。
- `origin/gh-pages` 為 `849cc87`（2026-09-21 02:25:26 +0800），部署 v0.6.1，建置來源 `2936174`。驗收指南 §15 記有線上 Playwright 手機模擬的畫面與 console 結果。

## 未決事項與下一步

1. `STEAM` 近白色飄字疊在白霧圈上對比偏低。先以實際畫面與玩家回饋確認可讀性，再決定是否改色；不要未經裁定擴大成整套視覺重做。
2. 原生 Android／iOS 的 120Hz、震動與延遲注入下真人手感仍未驗證；WebGL 試玩不能代替原生裝置驗收。
3. 符印牆血條蓋住木樁血條是既有已知問題，v0.6.1 未處理；若要修，先界定重現場景與驗收標準。
4. 下一批玩法尚無本紀錄可證實的凍結需求。先取得使用者方向，再依 `GDD.md` 與當批計畫推進。

## 交接檢查

開始工作時重新執行 `git status --short --branch`、`git log -5 --oneline`、`git log origin/gh-pages -1`；以當下結果為準。若要宣告新版本完成，附改動檔案、實跑指令與輸出，以及部署後的送達證明。
