// Minimal safe-mode recovery UI (1.19 B2). Rendered by main.tsx instead of App
// when the widget window has failed to boot SAFE_MODE_THRESHOLD times in a row
// (likely corrupt persisted state). Deliberately self-contained: inline styles,
// no design tokens / CSS files / stores, so the recovery screen can't itself be
// taken down by whatever is crashing the normal app.
import { clearBootAttempts, resetAllState } from './safeMode.ts';
import { t } from './i18n.ts';

export default function SafeMode(props: { windowKey: string }) {
  const reset = () => { resetAllState(); location.reload(); };
  const retry = () => { clearBootAttempts(props.windowKey); location.reload(); };

  const wrap: Record<string, string> = {
    position: 'fixed', inset: '0', display: 'flex', 'flex-direction': 'column',
    'align-items': 'center', 'justify-content': 'center', gap: '14px', padding: '32px',
    background: 'rgba(20,20,22,0.92)', color: 'rgba(255,255,255,0.9)',
    'font-family': "'Segoe UI', system-ui, sans-serif", 'text-align': 'center',
    'backdrop-filter': 'blur(40px)',
  };
  const btn: Record<string, string> = {
    'font-family': 'inherit', 'font-size': '13px', padding: '8px 16px', 'border-radius': '8px',
    border: '1px solid rgba(255,255,255,0.18)', background: 'transparent',
    color: 'inherit', cursor: 'pointer',
  };
  const primary: Record<string, string> = { ...btn, background: '#60cdff', color: '#08222e', border: 'none', 'font-weight': '600' };

  return (
    <div class="safe-mode" style={wrap}>
      <h1 style={{ 'font-size': '18px', 'font-weight': '600', margin: '0' }}>{t('safeMode.title')}</h1>
      <p style={{ 'font-size': '13px', color: 'rgba(255,255,255,0.6)', 'max-width': '380px', margin: '0' }}>
        The app failed to load several times in a row. Your saved settings may be corrupted.
        Reset them for a clean start, or try again if it was a one-off.
      </p>
      <div style={{ display: 'flex', gap: '10px', 'margin-top': '6px' }}>
        <button class="safe-mode-reset" style={primary} onClick={reset}>{t('safeMode.reset')}</button>
        <button class="safe-mode-retry" style={btn} onClick={retry}>{t('safeMode.retry')}</button>
      </div>
    </div>
  );
}
