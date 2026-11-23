# macOS 打包说明

## 产物

```powershell
pwsh packaging/build.ps1 -Runtime osx-arm64   # Apple silicon
pwsh packaging/build.ps1 -Runtime osx-x64     # Intel
```

生成：

```text
artifacts/<rid>/
├── gui/          HanVGamepad.Desktop（self-contained 单文件，含 wwwroot）
├── cli/          hanvgamepad（self-contained 单文件）
├── README.md
├── LICENSE
└── LICENSE-ORIGINAL
```

两者都是 self-contained：终端用户不需要安装 .NET 运行时。

## 运行前提：OpenJoystickDriver

macOS 上创建虚拟 HID 需要 Apple 的受限 entitlement
（`com.apple.developer.hid.virtual.device`），HanVGamepad 无法自带。发布说明里必须写清楚：

- 需要用户自行安装 **OpenJoystickDriver**（要求 macOS 12+）；
- 在 OJD 的设置里安装 `ojd` 命令行工具（`/usr/local/bin/ojd`）；
- 授予 OJD 输入监控与辅助功能；
- `ojd service start`。

安装步骤与桥接细节见 [docs/driver-setup/macos-ojd.md](../../docs/driver-setup/macos-ojd.md)。

## 分发形式

单文件可执行程序已可运行，但要双击启动、托盘图标正常工作，建议包成 `.app`：

```text
HanVGamepad.app/
└── Contents/
    ├── Info.plist          CFBundleExecutable = HanVGamepad.Desktop
    ├── MacOS/              gui/ 目录内容
    └── Resources/          （可选）图标
```

最小 `Info.plist` 需要：`CFBundleName`、`CFBundleIdentifier`、`CFBundleExecutable`、
`CFBundlePackageType=APPL`、`CFBundleShortVersionString`、`LSMinimumSystemVersion=12.0`、
`NSHighResolutionCapable=true`。

## 签名与公证

未签名 / ad-hoc 签名的 `.app` 会被 Gatekeeper 拦下，用户只能右键「打开」绕过。
要正常分发，需要：

1. Developer ID Application 证书签名；
2. `notarytool` 提交公证并 `stapler` 装订。

注意：这个签名只影响 HanVGamepad 自身的分发，**不能**让 HanVGamepad 获得虚拟 HID 权限——
那部分由 OJD 持有。

## 许可文件随包要求

- `LICENSE-ORIGINAL` 是第三方组件的原始许可文本，须随所有分发包附带（MIT 许可义务）；
- 程序「关于」界面显示 HanVGamepad 自身的 MIT 许可提示；
- 分发包不包含 OJD，只依赖用户自行安装的 OJD（MPL-2.0）。
