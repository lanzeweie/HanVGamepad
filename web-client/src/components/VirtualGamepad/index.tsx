import React, { useState, useEffect, useRef, useCallback } from 'react';
import type { GamepadLayout, ThemeType } from './types';
import { LAYOUT_KEY, DEFAULT_LAYOUT, THEMES, BTN_IDS, STICK_IDS } from './constants';
import { loadLayout, clamp } from './utils';
import { GpButton } from './GpButton';
import { GpJoystick } from './GpJoystick';
import { TopBar } from './TopBar';
import { ConnectionGate } from './ConnectionGate';
import { EditBar } from './EditBar';
import { useGamepadConnection } from './hooks/useGamepadConnection';
import { usePageLockdown } from './hooks/usePageLockdown';
import { LanguageProvider, useLanguage } from './LanguageContext';
import { fetchSavedLayouts, saveLayoutToServer } from './layoutApi';
import type { SavedLayout } from './layoutApi';

const VirtualGamepadInner: React.FC = () => {
  const { t } = useLanguage();

  // 挡住右键菜单 / 开发者工具 / 复制 / 刷新等会打断手柄操作的浏览器行为。
  usePageLockdown();

  const [theme, setTheme] = useState<ThemeType>(() => {
    const saved = localStorage.getItem('gamepad_theme');
    return saved === 'ps' || saved === 'nintendo' ? saved : 'xbox';
  });
  const controllerType = theme === 'ps' ? 1 : 0;
  const [layout, setLayout] = useState<GamepadLayout>(() => loadLayout(theme));

  // 方案存放在服务端：布局菜单里的「选择方案」读取这一份列表。
  const [savedLayouts, setSavedLayouts] = useState<SavedLayout[]>([]);
  const [currentLayoutName, setCurrentLayoutName] = useState<string | null>(null);
  const [layoutsLoadFailed, setLayoutsLoadFailed] = useState(false);

  useEffect(() => {
    fetchSavedLayouts()
      .then(list => { setSavedLayouts(list); setLayoutsLoadFailed(false); })
      .catch(() => setLayoutsLoadFailed(true));
  }, []);

  const [editMode, setEditMode] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [serverPassword, setServerPassword] = useState(() => localStorage.getItem('gamepad_pass') || '');
  // 振动默认开启：只记「显式关掉」的那次，没存过就是开。
  const [enableVib, setEnableVib] = useState<boolean>(() => localStorage.getItem('gamepad_vib') !== 'false');
  const [sensitivity, setSensitivity] = useState<number>(() => parseInt(localStorage.getItem('gamepad_sens') || '100', 10));
  const [activeButtons, setActiveButtons] = useState<Set<string>>(new Set());
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [isBarHidden, setIsBarHidden] = useState(false);
  const [selectedControl, setSelectedControl] = useState<string | null>(null);
  const [visibilityMenuOpen, setVisibilityMenuOpen] = useState(false);
  const [showHitboxes, setShowHitboxes] = useState(false);

  const canvasRef = useRef<HTMLDivElement>(null);

  // 服务器地址恒为当前页面的 origin：这个页面本就是那台 PC 提供的，
  // 再让用户填地址是自己给自己找麻烦。
  const actualServerUrl = window.location.origin;
  const {
    connStatus, latency, controllerId, notice,
    connect, dismissNotice, sendButton, sendJoystick
  } = useGamepadConnection({
    serverUrl: actualServerUrl, serverPassword, enableVib, sensitivity, controllerType, t
  });

  // Clear selection when exiting edit mode
  useEffect(() => { 
    if (!editMode) {
      setSelectedControl(null);
      setVisibilityMenuOpen(false);
    }
  }, [editMode]);

  // Fullscreen support
  useEffect(() => {
    const handleFullscreenChange = () => {
      const isFull = !!document.fullscreenElement;
      setIsFullscreen(isFull);
      if (!isFull) setIsBarHidden(false);
    };
    document.addEventListener('fullscreenchange', handleFullscreenChange);
    return () => {
      document.removeEventListener('fullscreenchange', handleFullscreenChange);
    };
  }, []);

  // 全屏，并尽量顺手锁一次横屏。
  // 能锁的只有 Android Chrome 这类：orientation.lock 必须先进全屏才生效；
  // iOS Safari 既没有元素级 requestFullscreen，也没有 orientation.lock —— 那边锁不住，
  // 只能靠连接门上那句「建议横屏」让用户自己转。
  const toggleFullscreen = async () => {
    try {
      if (!document.fullscreenElement) {
        if (document.documentElement.requestFullscreen) {
          await document.documentElement.requestFullscreen();
        }

        const orientation = (screen as unknown as {
          orientation?: { lock?: (orientation: string) => Promise<void> };
        }).orientation;

        try {
          await orientation?.lock?.('landscape');
        } catch {
          // 浏览器不认这个方向，或者当前不在全屏：继续，不打断用户。
        }
      } else {
        if (document.exitFullscreen) {
          await document.exitFullscreen();
        }

        // 退出全屏要解除锁定，否则用户回到页面还是被按在横屏。
        (screen as unknown as {
          orientation?: { unlock?: () => void };
        }).orientation?.unlock?.();
      }
    } catch (err) {
      console.error('Fullscreen error:', err);
    }
  };

  // Persist
  useEffect(() => { localStorage.setItem(`${LAYOUT_KEY}_${theme}`, JSON.stringify(layout)); }, [layout, theme]);
  useEffect(() => { localStorage.setItem('gamepad_theme', theme); }, [theme]);
  useEffect(() => { localStorage.setItem('gamepad_vib', enableVib.toString()); }, [enableVib]);
  useEffect(() => { localStorage.setItem('gamepad_sens', sensitivity.toString()); }, [sensitivity]);
  useEffect(() => { localStorage.setItem('gamepad_pass', serverPassword); }, [serverPassword]);

  // 锁屏 / 切应用 / 失焦：按住不放的键要在这里补一次「松开」。
  // 移动端这时只发 touchcancel，服务端会一直以为扳机按着 —— 重连后游戏里还是踩死的油门。
  const activeButtonsRef = useRef(activeButtons);
  activeButtonsRef.current = activeButtons;

  useEffect(() => {
    const releaseAll = () => {
      const held = activeButtonsRef.current;
      if (held.size === 0) return;
      held.forEach(id => sendButton(id, false));
      setActiveButtons(new Set());
      // 摇杆也归中：它同样可能停在最后的位置。
      STICK_IDS.forEach(sid => sendJoystick(sid, 0, 0));
    };

    const onVisibility = () => {
      if (document.visibilityState === 'hidden') releaseAll();
    };

    window.addEventListener('blur', releaseAll);
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      window.removeEventListener('blur', releaseAll);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [sendButton, sendJoystick]);

  const selectLayout = useCallback((name: string) => {
    const saved = savedLayouts.find(item => item.name === name);
    if (!saved || !saved.layout?.controls) return;

    setLayout(saved.layout);
    setCurrentLayoutName(name);
    setSelectedControl(null);
  }, [savedLayouts]);

  const saveLayout = useCallback(async () => {
    const name = window.prompt(t.layoutNamePrompt, currentLayoutName ?? '');
    const trimmed = name?.trim();
    if (!trimmed) return;

    try {
      const list = await saveLayoutToServer(trimmed, layout);
      setSavedLayouts(list);
      setCurrentLayoutName(trimmed);
      setLayoutsLoadFailed(false);
      setMenuOpen(false);
    } catch {
      alert(t.saveLayoutFailed);
    }
  }, [currentLayoutName, layout, t]);

  // Layout ops
  const updatePos = useCallback((id: string, x: number, y: number) => {
    setLayout(prev => ({ ...prev, controls: { ...prev.controls, [id]: { ...prev.controls[id], x, y } } }));
  }, []);

  const updateSize = useCallback((id: string, delta: number) => {
    setLayout(prev => {
      const ctrl = prev.controls[id] || { x: 50, y: 50 };
      const isStick = (STICK_IDS as readonly string[]).includes(id);
      const defaultW = isStick ? 120 : (THEMES[theme][id]?.w ?? 56);
      const defaultH = isStick ? 120 : (THEMES[theme][id]?.h ?? 56);
      const curW = ctrl.w ?? defaultW;
      const curH = ctrl.h ?? defaultH;
      return { ...prev, controls: { ...prev.controls, [id]: { ...ctrl, w: clamp(curW + delta, 24, 200), h: clamp(curH + delta, 24, 200) } } };
    });
  }, [theme]);

  const setSize = useCallback((id: string, size: number) => {
    setLayout(prev => {
      const ctrl = prev.controls[id] || { x: 50, y: 50 };
      return { ...prev, controls: { ...prev.controls, [id]: { ...ctrl, w: clamp(size, 24, 200), h: clamp(size, 24, 200) } } };
    });
  }, []);

  const setShape = useCallback((id: string, round: boolean) => {
    setLayout(prev => {
      const ctrl = prev.controls[id] || { x: 50, y: 50 };
      return { ...prev, controls: { ...prev.controls, [id]: { ...ctrl, round } } };
    });
  }, []);

  const nudgePos = useCallback((id: string, dx: number, dy: number) => {
    setLayout(prev => {
      const ctrl = prev.controls[id] || { x: 50, y: 50 };
      return {
        ...prev,
        controls: {
          ...prev.controls,
          [id]: {
            ...ctrl,
            x: clamp(ctrl.x + dx, 0, 95),
            y: clamp(ctrl.y + dy, 0, 95)
          }
        }
      };
    });
  }, []);

  const resetControl = useCallback((id: string) => {
    setLayout(prev => {
      const newControls = { ...prev.controls };
      const defaultPos = DEFAULT_LAYOUT.controls[id];
      if (defaultPos) {
        newControls[id] = { ...defaultPos };
      } else {
        delete newControls[id];
      }
      return { ...prev, controls: newControls };
    });
  }, []);

  const toggleVisibility = useCallback((id: string) => {
    setLayout(prev => {
      const defaultPos = DEFAULT_LAYOUT.controls[id];
      const ctrl = prev.controls[id] || (defaultPos ? { ...defaultPos } : { x: 50, y: 50 });
      return { ...prev, controls: { ...prev.controls, [id]: { ...ctrl, hidden: !ctrl.hidden } } };
    });
    setSelectedControl(prev => prev === id ? null : prev);
  }, []);

  const resetLayout = () => {
    setLayout(DEFAULT_LAYOUT);
    setCurrentLayoutName(null);
    setMenuOpen(false);
    setSelectedControl(null);
    setVisibilityMenuOpen(false);
  };

  return (
    <div className="gp-root">
      <TopBar
        connStatus={connStatus} latency={latency} controllerId={controllerId} isBarHidden={isBarHidden} setIsBarHidden={setIsBarHidden}
        isFullscreen={isFullscreen} toggleFullscreen={toggleFullscreen}
        editMode={editMode} setEditMode={setEditMode}
        menuOpen={menuOpen} setMenuOpen={setMenuOpen} visibilityMenuOpen={visibilityMenuOpen}
        setVisibilityMenuOpen={setVisibilityMenuOpen} resetLayout={resetLayout}
        layout={layout} toggleVisibility={toggleVisibility}
        showHitboxes={showHitboxes} setShowHitboxes={setShowHitboxes}
        savedLayouts={savedLayouts} currentLayoutName={currentLayoutName}
        layoutsLoadFailed={layoutsLoadFailed} selectLayout={selectLayout} saveLayout={saveLayout}
        enableVib={enableVib} setEnableVib={setEnableVib}
        sensitivity={sensitivity} setSensitivity={setSensitivity}
      />

      {/* 未连接时盖住整页：密码 / 连接 / 全屏 / 语言都在这里，连上才能用手柄。 */}
      <ConnectionGate
        connected={connStatus === 'on'}
        connecting={connStatus === 'ing'}
        notice={notice}
        serverPassword={serverPassword} setServerPassword={setServerPassword}
        theme={theme} setTheme={setTheme}
        connect={connect}
        toggleFullscreen={toggleFullscreen}
        isFullscreen={isFullscreen}
      />

      {notice && connStatus === 'on' && (
        <div className="gp-notice" onClick={dismissNotice}>{notice}</div>
      )}

      {/* ─── Canvas ─── */}
      <div
        ref={canvasRef}
        className={`gp-canvas ${editMode ? 'editing' : ''} ${connStatus === 'off' ? 'dimmed' : ''}`}
        onClick={() => {
          if (menuOpen) setMenuOpen(false);
          if (visibilityMenuOpen) setVisibilityMenuOpen(false);
        }}
      >
        {BTN_IDS.map(id => {
          const pos = layout.controls[id] || DEFAULT_LAYOUT.controls[id] || { x: 50, y: 50 };
          if (pos.hidden) return null;
          return (
            <GpButton
              key={id} id={id} cfg={THEMES[theme][id]}
              pos={pos}
              editMode={editMode}
              active={activeButtons.has(id)}
              isSelected={selectedControl === id}
              showHitboxes={showHitboxes}
              canvasRef={canvasRef}
              onDrag={(x, y) => updatePos(id, x, y)}
              onPress={() => {
                if (editMode) return;
                setActiveButtons(p => new Set(p).add(id));
                sendButton(id, true);
              }}
              onRelease={() => {
                if (editMode) return;
                setActiveButtons(p => { const s = new Set(p); s.delete(id); return s; });
                sendButton(id, false);
              }}
              onSelect={() => setSelectedControl(id)}
            />
          );
        })}
        {STICK_IDS.map(sid => {
          const pos = layout.controls[sid] || DEFAULT_LAYOUT.controls[sid] || { x: 50, y: 50 };
          if (pos.hidden) return null;
          return (
            <GpJoystick
              key={sid} id={sid}
              pos={pos}
              editMode={editMode}
              isSelected={selectedControl === sid}
              showHitboxes={showHitboxes}
              canvasRef={canvasRef}
              onDrag={(x, y) => updatePos(sid, x, y)}
              onMove={(x, y) => { if (!editMode) sendJoystick(sid.replace('Stick', ''), x, y); }}
              onSelect={() => setSelectedControl(sid)}
            />
          );
        })}

        <EditBar 
          editMode={editMode} selectedControl={selectedControl} layout={layout} theme={theme}
          updateSize={updateSize} setSize={setSize} setShape={setShape} nudgePos={nudgePos}
          resetControl={resetControl} setSelectedControl={setSelectedControl}
        />
      </div>
    </div>
  );
};

export const VirtualGamepad: React.FC = () => (
  <LanguageProvider>
    <VirtualGamepadInner />
  </LanguageProvider>
);
