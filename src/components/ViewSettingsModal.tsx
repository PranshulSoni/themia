import { Show } from 'solid-js';
import {
  viewStore, setViewIcon,
  isDemoView, deleteView, renameView,
} from '../views.ts';
import { ask } from '@tauri-apps/plugin-dialog';
import { Trash2 } from 'lucide-solid';
import IconPicker from './IconPicker.tsx';
import { t } from '../i18n.ts';
import './SettingsModal.css';

interface ViewSettingsBodyProps {
  // Called after the view is deleted (the deleted view's tree node
  // disappears reactively; the shell uses this to re-select a valid node).
  onAfterDelete?: () => void;
}

// The View settings content panel for the unified config window. Operates on
// the active view (`viewStore.activeId`). The shell sets that before
// rendering this. Sections (View / Grid / Danger Zone) render stacked on a
// single scrolling page. Theme and colors are global; see the Style page.
export default function ViewSettingsBody(props: ViewSettingsBodyProps) {
  const activeViewId = () => viewStore.activeId;
  const activeView = () => viewStore.list.find(v => v.id === activeViewId());
  const canDelete = () => viewStore.list.length > 1 && !isDemoView(activeViewId());

  return (
    <div class="settings-content cfg">
      <span class="section-label">{t('view.section.view')}</span>
      <div class="design-row">
        <div class="design-left">
          <span class="design-label">{t('view.name.label')}</span>
          <span class="design-desc">{t('view.name.desc')}</span>
        </div>
        <input
          class="view-name-edit-input kbd-focusable"
          type="text"
          value={activeView()?.name ?? ''}
          disabled={isDemoView(activeViewId())}
          onChange={(e) => {
            const id = activeViewId();
            const v = e.currentTarget.value.trim();
            if (v) renameView(id, v);
            // Restore the canonical stored name into the DOM. A whitespace-only
            // (rejected) or untrimmed edit that the store discards or normalises
            // to the same value doesn't change the bound signal, so Solid never
            // re-writes value={...} and the stale text would otherwise linger.
            e.currentTarget.value = activeView()?.name ?? '';
          }}
        />
      </div>
      <div class="design-row">
        <div class="design-left">
          <span class="design-label">{t('view.icon.label')}</span>
          <span class="design-desc">{t('view.icon.desc')}</span>
        </div>
        <IconPicker
          value={activeView()?.icon ?? 'home'}
          onChange={(v) => setViewIcon(activeViewId(), v)}
        />
      </div>

      <Show when={canDelete()}>
        <div class="divider" />
        <span class="section-label">{t('view.section.danger')}</span>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('view.deleteView.label')}</span>
            <span class="design-desc">{t('view.deleteView.desc')}</span>
          </div>
          <button
            class="btn-danger kbd-focusable"
            onClick={async () => {
              const view = activeView();
              if (!view) return;
              if (await ask(t('view.deleteView.ask', { name: view.name }), { title: t('view.deleteView.title'), kind: 'warning' })) {
                deleteView(activeViewId());
                props.onAfterDelete?.();
              }
            }}
          ><Trash2 size={13} strokeWidth={1.5} /> {t('view.deleteView.action')}</button>
        </div>
      </Show>
    </div>
  );
}
