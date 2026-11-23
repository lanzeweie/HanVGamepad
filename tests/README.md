# tests/ — 测试项目说明

本目录预留测试项目位置（`HanVGamepad.Server.Tests` / `HanVGamepad.Driver.Tests` /
`HanVGamepad.Integration.Tests`，见项目开发计划 §5）。

## 当前状态：未包含测试代码

本项目执行环境的工程策略明确禁止在任务中新增、编写或执行测试代码
（包括单元测试、回归测试、集成测试与测试依赖）。因此：

- `TryClaimSlot` 四级优先策略的单元测试（开发计划 Phase 1 验收项）未随本次交付编写；
- 其余测试项目同样未创建。

这不影响主解决方案的构建与运行：`HanVGamepad.slnx` 仅包含
`src/{server,driver,desktop,cli}` 四个生产项目。

## 建议的后续补充（由项目维护者按需实施）

| 测试项目 | 覆盖目标 |
|----------|---------|
| `HanVGamepad.Server.Tests` | `GamepadManager.TryClaimSlot` 四级策略（设备码→IP→干净槽→最小空闲）、`ScaleAxis` 边界、`UpdateSettingLive` 夹取 |
| `HanVGamepad.Driver.Tests` | `PadReport` → ViGEm / evdev 映射、`UinputVirtualPad` 差异事件生成 |
| `HanVGamepad.Integration.Tests` | SignalR Hub 全流程（SendOffer→SlotChanged→Release）、WebRTC DataChannel 报文解析 |

实现目标：`TryClaimSlot` 覆盖率 > 90%。
