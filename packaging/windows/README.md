# Windows 打包说明

## 产物

`pwsh packaging/build.ps1 -Runtime win-x64` 生成：

```text
artifacts/win-x64/
├── gui/          HanVGamepad.Desktop.exe（self-contained 单文件，含 wwwroot）
├── cli/          hanvgamepad.exe（self-contained 单文件）
├── README.md
├── LICENSE
└── LICENSE-ORIGINAL
```

## ViGEmBus 驱动安装器

- `src/driver/Windows/ViGEmBus_1.22.0_x64_x86_arm64.exe` 以 `EmbeddedResource`
  嵌入 `HanVGamepad.Driver.dll`。
- 首次运行时驱动管理器自动提取到 `%TEMP%\HanVGamepad\ViGEmBus_Setup.exe`，
  以 `runas`（UAC）执行 `/quiet /norestart` 静默安装，180 秒超时。
- 发布包分发时无需用户手动安装 ViGEmBus。

## NSIS / MSI（可选）

单文件 EXE 已可分发。如需安装包：

1. NSIS 脚本将 `gui/` 内容复制到 `%ProgramFiles%\HanVGamepad\`
2. 创建开始菜单与桌面快捷方式
3. 分发包内必须附带 `LICENSE` 与 `LICENSE-ORIGINAL` 两个许可文件

## 许可文件随包要求

- `LICENSE-ORIGINAL` 是第三方组件的原始许可文本，须随所有分发包附带（MIT 许可义务）；
- 程序「关于」界面显示 HanVGamepad 自身的 MIT 许可提示。
