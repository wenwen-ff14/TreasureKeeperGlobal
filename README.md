# TreasureKeeperGlobal｜國際服寶物庫整理助手

獨立衍生自 TreasureKeeper 0.1.9，目標為國際服 Dalamud API 15 / .NET 10。保留繁中操作介面；物品名稱、解讀選單與確認提示使用當前客戶端語言。支援英文、日文、德文、法文客戶端資料。

保留全域丟棄、庫內新物品補錄、每庫預設保留／丟棄、寶圖快捷解讀與自動確認。輸入 `/tkeep` 或 `/tmap`。深層地圖固定保留，自動丟棄需手動啟動。丟棄會移除一般背包整疊物品，包含原有數量。

使用獨立 InternalName、組件與設定檔，不匯入繁中版丟棄選擇。兩版使用相同指令，請勿同時載入。

寶物庫以國際服英文 ContentFinderCondition 名稱唯一比對，再取當前語言的區域及名稱；找不到或比對不唯一則停用該來源。包含原繁中快照未開放的 Vault Oneiron。物品可用性與可丟棄狀態仍由本機資料重新檢查。內建掉落是原版來源快照，不代表國際服完整掉落表；可使用自動補錄或手動新增。

## 建置

安裝 .NET 10 SDK，將官方 API 15 SDK 放在 `.sdk/`，或傳入 `-p:DalamudLibPath=...`。不使用繁中版或同層專案的 SDK。

```powershell
dotnet build TreasureKeeperGlobal/TreasureKeeperGlobal.csproj -c Release
dotnet run --project tests/TreasureKeeperGlobal.Validation -c Release
./scripts/Package.ps1
```

自訂插件庫網址：`https://raw.githubusercontent.com/wenwen-ff14/TreasureKeeperGlobal/main/pluginmaster.json`。在 Dalamud 設定的自訂插件庫新增此網址，再於插件安裝器搜尋 TreasureKeeperGlobal。原版歷史驗收與資料來源保存在 docs，不能視為國際服遊戲驗收。需在國際服確認 UI、補錄、解讀與丟棄；尤其更新客戶端後必須重新驗證原生函式。

官方版本依據：https://dalamud.dev/versions/v15/
授權 AGPL-3.0-or-later，保留原作者與 third-party 授權。

本次離線驗證：API 15 Release 建置成功（0 警告、0 錯誤）；2,361 項核心檢查通過。尚未在國際服遊戲內驗收。原作者：TreasureKeeper contributors。
本機已安裝專案專用 SDK：執行腳本前可設定 $env:PATH = "$PWD/.dotnet;$env:PATH"。

