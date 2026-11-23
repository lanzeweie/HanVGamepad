# Windows 打包说明

## 产物形态

Windows 发布**固定为目录展开形态**：exe 旁边就是散装的 dll 和 `wwwroot\`。
不使用 `PublishSingleFile` / `IncludeNativeLibrariesForSelfExtract` —— 单文件 exe 每次启动都要把自己解压到临时目录，冷启动慢，也没法在绿色包场景里直接替换文件。

## 一键构建

```powershell
pwsh windows-build.ps1              # 交互式：问清理、前端、绿色包、安装包、版本号
pwsh windows-build.ps1 -Clean       # 清理旧产物后全量构建
pwsh windows-build.ps1 -NoInstaller # 只出绿色包（没有 Inno Setup 时用）
pwsh windows-build.ps1 -SkipWeb     # 复用 src/desktop/wwwroot 现有前端产物
pwsh windows-build.ps1 -Version 1.1.0
```

流程：`npm run build`（web-client → `src/desktop/wwwroot`）→ `dotnet publish`（桌面端 + CLI，win-x64 self-contained）→ 组装绿色包目录 → 打 zip → ISCC 编译安装包。

## 产物

```text
artifacts/win-x64/HanVGamepad/                    绿色包目录（双击 HanVGamepad.Desktop.exe 直接跑）
├── HanVGamepad.Desktop.exe + 散装 dll + wwwroot\
├── cli\hanvgamepad.exe + 散装 dll + wwwroot\     headless 宿主
├── LICENSE
├── LICENSE-ORIGINAL
└── README.md

artifacts/packages/
├── HanVGamepad-<ver>-win-x64-portable.zip       绿色包压缩件
└── HanVGamepad-<ver>-win-x64-setup.exe          Inno Setup 安装包
```

## 安装包

- 脚本：`packaging/windows/HanVGamepad.iss`（UTF-8 with BOM）。发行者名等中文串必须靠 BOM 让 ISCC 按 UTF-8 读，
  存成无 BOM 会被当 ANSI 读成乱码。
- 编译器：Inno Setup 6，`ISCC.exe`。缺失时脚本给出安装命令：
  `winget install --id JRSoftware.InnoSetup --exact --source winget`
- 默认按用户安装（`PrivilegesRequired=lowest`），安装向导里可选「所有用户」（装到 `Program Files`）。
- 安装目录内容与绿色包一致；开始菜单快捷方式 + 可选桌面快捷方式。
- 卸载不删 `%APPDATA%\HanVGamepad`（settings.json、布局、日志留在原地，重装即恢复）。
- 界面语言：英文 + 简体中文。`languages\ChineseSimplified.isl` 是 Inno 官方 6.5.0+ 翻译，
  随仓库分发，不依赖本机 Inno 装没装翻译组件；`ShowLanguageDialog=no` 时按系统语言自动选，
  简体中文系统直接进中文向导，其他语言回落英文。语言文件缺失时脚本会退回纯英文编译，不会报错。

## ViGEmBus 驱动

- `src/driver/Windows/ViGEmBus_1.22.0_x64_x86_arm64.exe` 以 `EmbeddedResource` 嵌入 `HanVGamepad.Driver.dll`。
- 驱动不会被静默安装。启动服务时若检测到驱动缺失，程序弹窗询问；用户确认后才提取安装器并提权，
  **显示 ViGEmBus 自己的安装向导**，许可协议由用户在向导里亲自同意，安装完成后再回到主流程。
- 安装器超时按 10 分钟算（交互式，用户可能要看协议）；用户取消向导会明确提示「已取消，驱动仍未安装」。
- 安装包里不带驱动安装器，也**不要**在安装阶段就 UAC 装驱动——装完不启动程序的用户不该被弹权限框。


## 许可文件随包要求

- `LICENSE-ORIGINAL` 是第三方组件的原始许可文本，须随所有分发包附带（MIT 许可义务）；
- 程序「关于」界面显示 HanVGamepad 自身的 MIT 许可提示。
