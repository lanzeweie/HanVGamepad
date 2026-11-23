import React from 'react';
import { useLanguage } from './LanguageContext';

interface AboutModalProps {
  open: boolean;
  onClose: () => void;
}

export const AboutModal: React.FC<AboutModalProps> = ({ open, onClose }) => {
  const { t } = useLanguage();
  if (!open) return null;

  return (
    <div className="gp-overlay" onClick={onClose}>
      <div className="gp-about-modal" onClick={e => e.stopPropagation()}>

        {/* Header glow */}
        <div className="gp-about-glow" />

        {/* Logo / Title */}
        <div className="gp-about-header">
          <div className="gp-about-logo">🎮</div>
          <h2 className="gp-about-title">{t.aboutTitle}</h2>
          <p className="gp-about-version">{t.aboutTagline}</p>
        </div>

        {/* Project card */}
        <div className="gp-about-card">
          <p className="gp-about-made">{t.aboutMadeWith}</p>
          <p className="gp-about-name">{t.aboutName}</p>
        </div>

        {/* MIT license */}
        <p className="gp-about-mit">{t.aboutMIT}</p>

        {/* Close */}
        <button className="gp-about-close" onClick={onClose}>✕</button>
      </div>
    </div>
  );
};
