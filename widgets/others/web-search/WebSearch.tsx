import { createSignal } from 'solid-js';
import { Search } from 'lucide-solid';
import { invoke } from '../../../src/demo/invoke.ts';
import { logger } from '../../../src/debugLog.ts';
import { t } from '../../../src/i18n.ts';
import { ENGINES, resolveEngine } from './engines.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './WebSearch.css';

export interface WebSearchProps {
  engine?: string;
  headerMode?: HeaderMode;
  label?: string;
}

export default function WebSearch(props: WebSearchProps) {
  const [query, setQuery] = createSignal('');
  const engine = () => resolveEngine(props.engine);

  function submit(e: Event) {
    e.preventDefault();
    const q = query().trim();
    if (!q) return;
    const url = ENGINES[engine()].url(q);
    invoke('open_url', { url }).catch(err => logger.error('web-search', `open_url failed: ${err}`));
    setQuery('');
  }

  return (
    <div class="websearch-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Search}
        title={props.label || t('widget.webSearch.label')}
      />
      <div class="widget-body">
      <form class="websearch-form" onSubmit={submit}>
        <span class="websearch-icon"><Search size={13} strokeWidth={1.5} /></span>
        <input
          class="websearch-input"
          type="text"
          placeholder={`Search ${ENGINES[engine()].name}…`}
          value={query()}
          onInput={e => setQuery(e.currentTarget.value)}
          spellcheck={false}
          autocomplete="off"
        />
      </form>
      </div>
    </div>
  );
}
