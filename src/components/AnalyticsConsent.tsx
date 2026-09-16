import { ChartNoAxesColumn } from 'lucide-solid';
import { t, tm } from '../i18n.ts';
import { invoke } from '../ipc.ts';
import { logger } from '../debugLog.ts';
import { setAnalyticsConsent } from '../analytics.ts';

const PRIVACY_URL = 'https://themia.app/legal/privacy';

interface AnalyticsConsentProps {
  /** Called once the user has answered, so the card can be unmounted. */
  onAnswered: () => void;
}

/**
 * [S-27] The first-run analytics consent card.
 *
 * Storing an install identifier for analytics needs consent first, so nothing
 * is sent — and no identifier is even minted — until this is answered ([S-26]).
 * The card is deliberately NOT a modal: it cannot block a desktop the user came
 * here to use, and a prompt nobody can dismiss is not a free choice. Ignoring
 * it leaves the decision `unset`, which sends exactly as much as "no"; the
 * question is simply asked again next launch.
 *
 * Both buttons call the same command as the Settings toggle — one decision,
 * one code path. Answering "no" sends nothing at all (there was never a consent
 * to announce leaving, [S-07]).
 *
 * Only one window ever renders this: App.tsx claims the prompt from the backend
 * so a three-monitor setup asks once, not three times ([S-27]).
 */
export default function AnalyticsConsent(props: AnalyticsConsentProps) {
  const answer = (granted: boolean): void => {
    props.onAnswered();
    setAnalyticsConsent(granted)
      .catch(e => logger.error('analytics', `analytics_set_consent failed: ${e}`));
  };

  return (
    <div class="consent-card" data-themia-interactive="true" role="dialog" aria-label={tm('app.consent.title')()}>
      <div class="consent-head">
        <ChartNoAxesColumn size={13} strokeWidth={1.5} />
        <span class="consent-title">{tm('app.consent.title')()}</span>
      </div>
      <p class="consent-body">{tm('app.consent.body')()}</p>
      <div class="consent-actions">
        <button
          class="consent-link"
          onClick={() => invoke('open_url', { url: PRIVACY_URL })
            .catch(e => logger.error('analytics', `open_url failed: ${e}`))}
        >
          {t('app.consent.more')}
        </button>
        <span class="consent-spacer" />
        <button class="consent-btn" onClick={() => answer(false)}>{t('app.consent.decline')}</button>
        <button class="consent-btn is-primary" onClick={() => answer(true)}>{t('app.consent.accept')}</button>
      </div>
    </div>
  );
}
