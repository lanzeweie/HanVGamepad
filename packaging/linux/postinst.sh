#!/bin/bash
# HanVGamepad deb/rpm 安装后配置：加载 uinput、装 udev rule、建服务用户。
# 由包管理器以 root 执行。
set -e

echo "[hanvgamepad] configuring uinput..."

# 1. 内核模块 + 持久化
modprobe uinput || true
echo uinput > /etc/modules-load.d/hanvgamepad.conf

# 2. udev rule
install -m 0644 /opt/hanvgamepad/systemd/99-hanvgamepad.rules /etc/udev/rules.d/99-hanvgamepad.rules
udevadm control --reload-rules
udevadm trigger

# 3. 服务用户（无登录壳，仅跑服务）
if ! id -u hanvgamepad >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin hanvgamepad
fi
usermod -aG input hanvgamepad

# 4. systemd unit
install -m 0644 /opt/hanvgamepad/systemd/hanvgamepad.service /etc/systemd/system/hanvgamepad.service
systemctl daemon-reload

echo "[hanvgamepad] done. Enable the service with: systemctl enable --now hanvgamepad"
