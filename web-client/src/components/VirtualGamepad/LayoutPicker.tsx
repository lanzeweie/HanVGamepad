import React from 'react';
import { useLanguage } from './LanguageContext';
import type { SavedLayout } from './layoutApi';

interface LayoutPickerProps {
  open: boolean;
  layouts: SavedLayout[];
  currentName: string | null;
  loadFailed: boolean;
  onSelect: (name: string) => void;
  onClose: () => void;
}

/** 服务端方案列表：菜单里的「选择方案」和顶栏下拉共用同一份数据。 */
export const LayoutPicker: React.FC<LayoutPickerProps> = ({
  open, layouts, currentName, loadFailed, onSelect, onClose
}) => {
  const { t } = useLanguage();

  if (!open) return null;

  return (
    <div className="gp-overlay" onClick={onClose}>
      <div className="gp-layout-modal" onClick={e => e.stopPropagation()}>
        <div className="gp-layout-header">
          <span className="gp-layout-title">{t.selectLayout}</span>
          <button className="gp-layout-close" onClick={onClose} aria-label={t.closePanel}>✕</button>
        </div>

        {loadFailed ? (
          <p className="gp-layout-empty">{t.loadLayoutsFailed}</p>
        ) : layouts.length === 0 ? (
          <p className="gp-layout-empty">{t.noSavedLayouts}</p>
        ) : (
          <div className="gp-layout-list">
            {layouts.map(item => (
              <button
                key={item.name}
                className={`gp-layout-item ${item.name === currentName ? 'active' : ''}`}
                onClick={() => onSelect(item.name)}
              >
                {item.name}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
