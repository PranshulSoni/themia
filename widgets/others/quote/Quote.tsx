import { createSignal, createMemo } from 'solid-js';
import { Quote as QuoteIcon, Shuffle } from 'lucide-solid';
import { QUOTES, quoteForDay } from './quotes.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import './Quote.css';

export interface QuoteProps {
  headerMode?: HeaderMode;
  label?: string;
}

export default function Quote(props: QuoteProps) {
  // null = show today's quote; a number = a manually shuffled index (ephemeral,
  // resets to today's on reload; "quote of the day" is the persistent state).
  const [override, setOverride] = createSignal<number | null>(null);

  const quote = createMemo(() => {
    const o = override();
    return o === null ? quoteForDay(new Date()) : QUOTES[o % QUOTES.length];
  });

  function shuffle() {
    setOverride(Math.floor(Math.random() * QUOTES.length));
  }

  return (
    <div class="quote-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={QuoteIcon}
        title={props.label || t('widget.quote.label')}
        actions={
          <button class="quote-shuffle" onClick={shuffle} title={t('quote.shuffle')}>
            <Shuffle size={13} strokeWidth={1.5} />
          </button>
        }
      />
      <div class="widget-body quote-body">
        <blockquote class="quote-text">“{quote().text}”</blockquote>
        <div class="quote-author">- {quote().author}</div>
      </div>
    </div>
  );
}
