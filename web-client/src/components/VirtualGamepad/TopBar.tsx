import React, { useState } from 'react';
import type { GamepadLayout } from './types';
import { BTN_IDS, STICK_IDS } from './constants';
import { useLanguage } from './LanguageContext';
import { LayoutPicker } from './LayoutPicker';
import type { SavedLayout } from './layoutApi';

interface TopBarProps {
  connStatus: 'off' | 'ing' | 'on';
  latency: number | null;
  controllerId: number | null;
  isBarHidden: boolean;
  setIsBarHidden: (hidden: boolean) => void;
  isFullscreen: boolean;
  toggleFullscreen: () => void;
  editMode: boolean;
  setEditMode: (mode: boolean) => void;
  menuOpen: boolean;
  setMenuOpen: (open: boolean) => void;
  visibilityMenuOpen: boolean;
  setVisibilityMenuOpen: (open: boolean) => void;
  resetLayout: () => void;
  layout: GamepadLayout;
  toggleVisibility: (id: string) => void;
  showHitboxes: boolean;
  setShowHitboxes: (show: boolean) => void;
  // 从设置弹窗搬出来的手柄参数：直接摊在这一行里，不再有弹窗。
  savedLayouts: SavedLayout[];
  currentLayoutName: string | null;
  layoutsLoadFailed: boolean;
  selectLayout: (name: string) => void;
  saveLayout: () => void;
  enableVib: boolean;
  setEnableVib: (enable: boolean) => void;
  sensitivity: number;
  setSensitivity: (sens: number) => void;
}

/**
 * 顶栏是手柄界面的唯一一行：状态 + 所有可调项。
 * 配置方案、按键风格、启动振动、全屏都直接摆在这一行上，
 * 原来的设置弹窗已经拆掉，不再有第二处入口。
 */
export const TopBar: React.FC<TopBarProps> = ({
  connStatus, latency, controllerId, isBarHidden, setIsBarHidden, isFullscreen, toggleFullscreen,
  editMode, setEditMode, menuOpen, setMenuOpen, visibilityMenuOpen, setVisibilityMenuOpen,
  resetLayout, layout, toggleVisibility,
  showHitboxes, setShowHitboxes,
  savedLayouts, currentLayoutName, layoutsLoadFailed, selectLayout, saveLayout,
  enableVib, setEnableVib, sensitivity, setSensitivity
}) => {
  const { t } = useLanguage();
  const [pickerOpen, setPickerOpen] = useState(false);

  // 延迟常驻：没有测量值就显示 1ms，避免它从无到有把后面的按钮挤走。
  const shownLatency = latency ?? 1;

  return (
    <>
      <div className={`gp-bar ${isBarHidden ? 'hidden' : ''}`}>
        <span className={`gp-dot ${connStatus}`} />
        <span className="gp-bar-status">
          {connStatus === 'on' ? t.connected : connStatus === 'ing' ? t.connecting : t.disconnected}
        </span>
        <span className={`gp-latency ${shownLatency < 20 ? 'good' : shownLatency < 50 ? 'ok' : 'bad'}`}>
          {shownLatency}ms
        </span>
        {connStatus === 'on' && controllerId !== null && (
          <span className="gp-bar-status" style={{ color: '#3b82f6', background: 'rgba(59, 130, 246, 0.1)', padding: '2px 6px', borderRadius: '4px' }}>
            P{controllerId + 1} ({controllerId})
          </span>
        )}
        {/* row-reverse 下这个位置在槽位徽章（P1 (0)）的左侧。 */}
        <button
          className={`gp-bar-btn ${editMode ? 'done' : ''}`}
          onClick={() => setEditMode(!editMode)}
        >
          {editMode ? t.done : t.edit}
        </button>
        <div className="gp-spacer" />

        {/* 手柄参数：启动振动 / 灵敏度 —— 直接摆在这一行。
            方案存取和手柄类型不在这里：方案走「布局菜单」，设备类型走连接门。 */}
        <button
          className={`gp-bar-btn gp-bar-toggle ${enableVib ? 'on' : 'off'}`}
          onClick={() => setEnableVib(!enableVib)}
          aria-label={t.enableVibration}
        >
          {t.enableVibration}
          <span className="gp-bar-toggle-state">{enableVib ? t.on : t.off}</span>
        </button>

        <label className="gp-bar-slider" aria-label={`${t.sensitivity} (${sensitivity}%)`}>
          <span className="gp-bar-slider-label">{t.sensitivity}</span>
          <input
            type="range"
            min="10" max="200"
            value={sensitivity}
            onChange={e => setSensitivity(parseInt(e.target.value, 10))}
          />
          <span className="gp-bar-slider-value">{sensitivity}%</span>
        </label>

        <button className="gp-bar-btn" onClick={toggleFullscreen}>
          {isFullscreen ? t.minimize : t.fullscreen}
        </button>

        {isFullscreen && (
          <button className="gp-bar-btn" onClick={() => setIsBarHidden(true)}>
            {t.hide}
          </button>
        )}
        <button
          className={`gp-bar-btn ${showHitboxes ? 'done' : ''}`}
          onClick={() => setShowHitboxes(!showHitboxes)}
        >
          {t.showTouchArea}
        </button>
        {editMode && (
          <div className="gp-menu-wrap">
            <button className="gp-bar-btn" onClick={() => { setVisibilityMenuOpen(!visibilityMenuOpen); setMenuOpen(false); }}>
              {t.buttons}
            </button>
            {visibilityMenuOpen && (
              <div className="gp-dropdown" style={{ maxHeight: '60vh', overflowY: 'auto', minWidth: '150px' }}>
                {[...BTN_IDS, ...STICK_IDS].map(id => {
                  const isHidden = layout.controls[id]?.hidden === true;
                  return (
                    <button key={id} onClick={() => toggleVisibility(id)} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <span>{id === 'LeftStick' ? t.leftStick : id === 'RightStick' ? t.rightStick : id}</span>
                      <span className="gp-vis-state">{isHidden ? t.hideControl : t.show}</span>
                    </button>
                  );
                })}
              </div>
            )}
          </div>
        )}

        <div className="gp-menu-wrap">
          <button className="gp-bar-btn" onClick={() => { setMenuOpen(!menuOpen); setVisibilityMenuOpen(false); }}>{t.menu}</button>
          {menuOpen && (
            <div className="gp-dropdown promptfont">
              <button onClick={resetLayout}>{t.reset}</button>
              <button onClick={saveLayout}>{t.saveLayout}</button>
              <button onClick={() => { setPickerOpen(true); setMenuOpen(false); }}>{t.selectLayout}</button>
            </div>
          )}
        </div>
      </div>

      {isBarHidden && (
        <button
          className="gp-show-bar-btn"
          onClick={() => setIsBarHidden(false)}
        >
          {t.showBar}
        </button>
      )}
      <LayoutPicker
        open={pickerOpen}
        layouts={savedLayouts}
        currentName={currentLayoutName}
        loadFailed={layoutsLoadFailed}
        onSelect={name => { selectLayout(name); setPickerOpen(false); }}
        onClose={() => setPickerOpen(false)}
      />
    </>
  );
};
