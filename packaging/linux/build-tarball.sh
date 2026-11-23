#!/bin/bash
# HanVGamepad Linux 打包辅助：生成 .tar.gz（portable）。
# .deb / .rpm 建议用 fpm 或发行版标准工具链包装 artifacts/linux-x64 下的产物。
set -e

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ARTIFACTS="$ROOT/artifacts"
OUT="$ARTIFACTS/packages"
VERSION="1.0.0"

mkdir -p "$OUT"

for RID in linux-x64 linux-arm64; do
    SRC="$ARTIFACTS/$RID"
    if [ ! -d "$SRC" ]; then
        echo "skip $RID (not built; run: pwsh packaging/build.ps1 -Runtime $RID)"
        continue
    fi

    PKG="hanvgamepad-$VERSION-$RID"
    STAGE="$OUT/$PKG"
    mkdir -p "$STAGE"

    cp -r "$SRC"/* "$STAGE/"

    # udev rule 与 modules-load 配置随包分发（安装脚本用）。
    mkdir -p "$STAGE/systemd"
    cp "$ROOT/packaging/linux/hanvgamepad.service" "$STAGE/systemd/"
    cp "$ROOT/packaging/linux/99-hanvgamepad.rules" "$STAGE/systemd/"
    cp "$ROOT/packaging/linux/postinst.sh" "$STAGE/systemd/"
    chmod +x "$STAGE/systemd/postinst.sh"

    tar -czf "$OUT/$PKG.tar.gz" -C "$OUT" "$PKG"
    rm -rf "$STAGE"
    echo "built $OUT/$PKG.tar.gz"
done

echo "Note: license texts (LICENSE / LICENSE-ORIGINAL) ship with every package."
