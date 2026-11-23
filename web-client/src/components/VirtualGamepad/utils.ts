import type { ThemeType, GamepadLayout } from './types';
import { LAYOUT_KEY, DEFAULT_LAYOUT } from './constants';

/**
 * 当前布局按手柄类型缓存在本地；服务端方案只是一份可命名的快照。
 * 旧版本把布局按「方案 + 手柄类型」分键，默认方案的键是 `_default_<theme>`，
 * 这里顺手迁移一次，用户升级后不会突然变回默认布局。
 */
export function loadLayout(theme: ThemeType): GamepadLayout {
  try {
    let raw = localStorage.getItem(`${LAYOUT_KEY}_${theme}`);
    if (!raw) {
      raw = localStorage.getItem(`${LAYOUT_KEY}_default_${theme}`);
      if (raw) {
        localStorage.setItem(`${LAYOUT_KEY}_${theme}`, raw);
      }
    }
    if (raw) { const p = JSON.parse(raw); if (p.controls) return p; }
  } catch { /* ignore */ }
  return DEFAULT_LAYOUT;
}

export function clamp(v: number, min: number, max: number) { 
  return Math.max(min, Math.min(max, v)); 
}
