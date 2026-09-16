import { createSignal, Show } from 'solid-js';
import { Mail, Check } from 'lucide-solid';
import { newsletterState, subscribeNewsletter, resetNewsletter } from '../license.ts';
import { t } from '../i18n.ts';

export default function NewsletterRow() {
  const [email, setEmail] = createSignal('');

  async function submit() {
    const v = email().trim();
    if (!v) return;
    const ok = await subscribeNewsletter(v);
    if (ok) setEmail('');
  }

  return (
    <div class="design-row newsletter-row">
      <div class="design-left">
        <span class="design-label">{t('settings.newsletter.label')}</span>
        <span class="design-desc">{t('settings.newsletter.desc')}</span>
      </div>
      <Show
        when={!newsletterState.subscribed}
        fallback={
          <div class="newsletter-done">
            <span class="newsletter-ok">
              <Check size={13} strokeWidth={1.5} /> {t('settings.newsletter.success')}
              <Show when={newsletterState.email}>
                <span class="newsletter-email"> · {newsletterState.email}</span>
              </Show>
            </span>
            <button class="btn-secondary" onClick={resetNewsletter}>
              {t('settings.newsletter.change')}
            </button>
          </div>
        }
      >
        <div class="newsletter-activate">
          <input
            type="email"
            class="license-input newsletter-input"
            placeholder={t('settings.newsletter.placeholder')}
            value={email()}
            onInput={(e) => setEmail(e.currentTarget.value)}
            onKeyDown={(e) => { if (e.key === 'Enter' && email().trim()) submit(); }}
          />
          <button
            class="btn-primary"
            disabled={newsletterState.loading || !email().includes('@')}
            onClick={submit}
          >
            <Mail size={13} strokeWidth={1.5} />{' '}
            {newsletterState.loading ? t('settings.newsletter.loading') : t('settings.newsletter.action')}
          </button>
        </div>
        <Show when={newsletterState.error}>
          <span class="license-error newsletter-error">{newsletterState.error}</span>
        </Show>
      </Show>
    </div>
  );
}
