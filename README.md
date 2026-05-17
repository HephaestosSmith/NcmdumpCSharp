# NcmdumpGUI (Create By Claude Sonnet 4.6 High)

網易雲音樂 NCM 檔案解密工具 — GUI 版本

## 簡介

以原生 C# 重新實作的網易雲音樂 NCM 解密工具，完整還原原 C++ 版本的所有功能，並額外提供 **WPF 圖形介面**（`NcmdumpCSharpGui`），支援批次解密、重複檔案掃描與簡繁轉換。

## 功能特色

### 核心解密（CLI / 類別庫）
- ✅ NCM 檔案格式識別與驗證（魔數檢查）
- ✅ AES-ECB 金鑰解密
- ✅ RC4 金鑰盒 XOR 音訊解密
- ✅ 自動識別輸出格式（MP3 / FLAC）
- ✅ 元數據解析與寫入（標題、藝術家、專輯）
- ✅ 專輯封面提取與嵌入
- ✅ 批次處理與遞迴子目錄支援

### 圖形介面（NcmdumpCSharpGui）
- 🖥️ 現代化 WPF 深色主題介面
- ⚡ 多執行緒並行解密，可調整並行度（上限為實體核心數）
- 🈶 簡體 → 繁體中文自動轉換（檔名、資料夾名稱、歌曲標籤）
- 📋 即時記錄面板（自動捲動）
- 📁 可選複製非 NCM 檔案至輸出目錄
- 🔍 重複檔案掃描器（依元數據或檔名分組，支援勾選批次刪除）

## 系統需求

- .NET 10.0 或更高版本
- Windows 10 / 11（圖形介面需要 Windows；CLI 跨平台）

## 安裝

### 從原始碼編譯

```bash
git clone <repository-url>
cd NcmdumpCSharp

# 編譯 CLI
dotnet build NcmdumpCSharp -c Release

# 編譯 GUI
dotnet build NcmdumpCSharpGui -c Release

# 發佈 GUI（單一資料夾）
dotnet publish NcmdumpCSharpGui -c Release -o publish
```

### 下載預編譯版本

前往 [Releases](https://github.com/HephaestosSmith/NcmdumpCSharp/releases) 頁面下載對應平台的預編譯版本（如有提供）。

## 使用方式

### 圖形介面

執行 `NcmdumpCSharpGui.exe`，介面分為兩個索引標籤：

**🔓 解密工具**
1. 選擇「來源資料夾」（含 NCM 檔案）
2. 選擇「輸出資料夾」
3. 視需要開啟「翻譯繁體」或「複製非 NCM 檔案」
4. 調整並行執行緒數（預設為實體核心數）
5. 按「▶  開始」執行

**🔍 重複掃描**
1. 選擇要掃描的資料夾
2. 按「🔍 開始掃描」
3. 工具依元數據（標題 + 藝術家）或檔名後綴 `(1)`、`(2)` 分組重複檔案
4. 勾選欲刪除的檔案後按「🗑 刪除已選取」

### 命令列工具

> 請將 `ncmdump-csharp` 替換為實際可執行檔名稱。

```bash
# 顯示說明
ncmdump-csharp --help

# 顯示版本
ncmdump-csharp --version

# 處理單一或多個檔案
ncmdump-csharp file1.ncm file2.ncm

# 處理整個目錄（遞迴）
ncmdump-csharp -d /path/to/music -r

# 指定輸出目錄
ncmdump-csharp -d /path/to/music -r -o /path/to/output
```

### 作為類別庫使用

```csharp
using NcmdumpCSharp.Core;

// 解密並寫入檔案
using var crypt = new NeteaseCrypt("path/to/file.ncm");
crypt.Dump("output/directory");
crypt.FixMetadata();
Console.WriteLine(crypt.DumpFilePath);

// 非同步版本
await crypt.DumpAsync("output/directory");

// 解密至記憶體串流
using var ms = crypt.DumpToStream();
```

## 專案結構

```
NcmdumpCSharp/              ← CLI / 核心類別庫
├── Core/
│   └── NeteaseCrypt.cs         # 核心解密類別
├── Crypto/
│   ├── AesHelper.cs            # AES 解密輔助
│   └── Base64Helper.cs         # Base64 解碼輔助
├── Models/
│   └── NeteaseMusicMetadata.cs # 音樂元數據模型
└── Program.cs                  # CLI 入口點

NcmdumpCSharpGui/           ← WPF 圖形介面
├── Converters/
│   └── Converters.cs           # WPF 值轉換器
├── Helpers/
│   └── RelayCommand.cs         # ICommand 實作
├── Models/
│   ├── LogEntry.cs             # 記錄條目
│   ├── ProcessOptions.cs       # 解密選項
│   └── ProgressReport.cs       # 進度回報
├── Services/
│   ├── ChineseConverter.cs     # 簡繁轉換（LCMapStringEx）
│   └── DecryptionService.cs    # 多執行緒解密服務
├── ViewModels/
│   ├── MainViewModel.cs        # 主視窗 ViewModel
│   └── DuplicateScanViewModel.cs # 重複掃描 ViewModel
├── MainWindow.xaml / .cs       # 主視窗
└── App.xaml / .cs              # 應用程式入口
```

## 技術細節

| 項目 | 說明 |
|------|------|
| 解密流程 | 魔數驗證 → AES-ECB 金鑰解密 → RC4 XOR 音訊資料 → 格式偵測 → 元數據寫入 |
| 簡繁轉換 | Windows API `LCMapStringEx`（`LCMAP_TRADITIONAL_CHINESE`），透過 `Marshal.AllocHGlobal` 避免 NUL 截斷問題 |
| 多執行緒 | `Parallel.ForEachAsync` + `CancellationTokenSource`，並行度上限 = `Environment.ProcessorCount / 2`（實體核心數） |
| 重複偵測 | 優先讀取 ATL 元數據（標題 + 藝術家）；無元數據時以正規表示式去除 `(1)`、`(2)` 後綴比對檔名 |

### 相依套件

- **z440.atl.core** — 音訊元數據讀寫（MP3 / FLAC / …）
- **System.Text.Json** — JSON 解析
- **System.CommandLine** — CLI 參數解析
- **System.Security.Cryptography** — AES 加密演算法

## 授權

本專案採用與原版相同的授權條款，詳見 [LICENSE](LICENSE) 檔案。

## 貢獻

歡迎提交 Issue 與 Pull Request！

## 致謝

- 原版本作者：[Mioter/NcmdumpCSharp](https://github.com/Mioter/NcmdumpCSharp)