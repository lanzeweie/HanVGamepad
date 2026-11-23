# HanVGamepad Web Client

HanVGamepad 的移动端触控手柄页面（React 19 + Vite + TypeScript）。
构建产物内嵌到宿主 `src/desktop/wwwroot/`，由宿主服务直接提供。

## 开发

```bash
npm install
npm run dev      # 本地开发服务器
npm run build    # 构建产物输出到 ../src/desktop/wwwroot/
```

## 结构

```text
src/
├── App.tsx / main.tsx            入口
└── components/VirtualGamepad/    触控手柄主界面
    ├── index.tsx                 手柄布局与触控采集
    ├── ConnectionGate.tsx        连接门（选槽 / 密码 / 连接）
    ├── GpButton.tsx / GpJoystick.tsx   按键与摇杆控件
    ├── TopBar.tsx / EditBar.tsx  顶栏与布局编辑
    ├── AboutModal.tsx            关于
    ├── hooks/                    连接管理 / 页面锁定
    ├── i18n.ts                   中 / 英 / 阿三语
    └── constants.ts / types.ts / utils.ts
```

## 协议

与宿主通过 SignalR（`/gamepadhub`）信令 + WebRTC DataChannel 通信，
消息格式见 `docs/protocol/datachannel.md`。

## 许可证

本目录是 HanVGamepad 的一部分，随项目整体采用
[PolyForm Noncommercial License 1.0.0](../LICENSE)，完整条款见仓库根目录的 `LICENSE`。

```text
Required Notice: Copyright (c) 2026 HanVGamepad Contributors
```
