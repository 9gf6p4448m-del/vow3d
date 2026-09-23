# VOW 誓約 — Codex 專案入口

- 回覆使用繁體中文。先讀 `docs/HANDOFF.md` 核對目前分支、版本、送達與待辦，再按任務讀 `docs/PHASE1_ACCEPTANCE_GUIDE.md`、當批計畫及相關程式。不要把 `README.md` 的設計願景當成已驗證的工程成果。
- 本專案是 Unity 2022.3.62f1、C#、URP；`ProjectSettings/ProjectVersion.txt` 與 `Packages/manifest.json` 是實際版本來源。`ARCHITECTURE.md`、`GDD.md` 是架構與設計來源。Three.js 專用技能的程式碼、效能指標與工具不直接套用到 VOW。
- 凍結的驗收條件見當批計畫。修 bug、補測試可依既定條件進行；改玩法、數值、手感、風控或美術方向前，先向使用者確認。不可為讓測試過關而自行放寬門檻。
- 程式改動先跑相關 Unity EditMode／PlayMode 與 `Tools/DotnetCheck/verify.sh`；畫面或手感改動還要查看實際執行畫面。測試全綠不等於原生手機手感已驗收。
- WebGL 試玩版有部署步驟。宣告送達前，核對 `origin/gh-pages` 的提交、線上版本列與實際互動畫面；若 PWA 顯示舊版，提醒玩家關閉分頁重開或強制重新整理。
