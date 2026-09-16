import { createSignal, createMemo, For, Show, onCleanup } from 'solid-js';
import { Search, X, Crown, Ban, FolderTree, Server, ArrowLeft, Layers, PanelBottom } from 'lucide-solid';
import registry from '../widgets/registry.tsx';
import type { WidgetDefinition } from '../widgets/registry.tsx';
import { atFreeWidgetLimit, isContainerType } from '../layout.ts';
import { globalDesign } from '../design.ts';
import { setPreviewDemo } from '../demo/invoke.ts';
import { nextIndexLinear } from '../input/kbdNav.ts';
import Dropdown from './Dropdown.tsx';
import WidgetRowPreview from './WidgetRowPreview.tsx';
import DevAspectPanel from './DevAspectPanel.tsx';
import { spanFromSize } from '../demo/previewAspects.ts';
import { computePickerGroups, KNOWN_CATEGORIES, NO_PROVIDER_BUCKET, type GroupBy } from './pickerGroups.ts';
import { indexWidget, rankWidgets, type IndexedWidget } from './widgetSearch.ts';
import { WIDGET_KEYWORDS } from '../widgets/searchKeywords.ts';
import { en } from '../locales/en.ts';
import { t, tOr } from '../i18n.ts';
import './AddWidgetPage.css';

function translateHeading(heading: string, mode: string): string {
  // The `General` bucket only appears in provider mode, so its translation
  // must run before the category-only guard below (else it stays untranslated).
  if (heading === NO_PROVIDER_BUCKET) return t('app.pickerGroupGeneral');
  if (mode !== 'category') return heading;
  if (KNOWN_CATEGORIES.has(heading)) return t(`category.${heading}`);
  return heading;
}

const groupByOptions = () => [
  { label: t('app.pickerGroupNone'),     value: 'none',     icon: () => <Ban size={13} strokeWidth={1.5} /> },
  { label: t('app.pickerGroupCategory'), value: 'category', icon: () => <FolderTree size={13} strokeWidth={1.5} /> },
  { label: t('app.pickerGroupProvider'), value: 'provider', icon: () => <Server size={13} strokeWidth={1.5} /> },
];

interface AddWidgetPageProps {
  /** Called when the user picks a widget type to add. */
  onPick: (type: string, def: WidgetDefinition) => void;
  /** Called when the user leaves the picker via the Back button. */
  onBack: () => void;
  /** Display name of the container being filled, when the page was opened from
   *  one ([S-34]). Shows the target in the topbar and hides container types,
   *  which never nest. */
  intoContainer?: string;
  /** The pick goes to the tray collection (Tray.SPEC [S-01]). Same two effects
   *  as `intoContainer`: name the destination, and drop container types, which
   *  arrange sibling tiles a one-widget flyout does not have. */
  intoTray?: boolean;
}

// Widget picker, hosted as a page inside the unified config window. A topbar
// with a "Group by" dropdown + search drives a vertical list; each row shows
// icon + label + short description + a live preview rendered with demo data.
//
// `setPreviewDemo(true)` runs synchronously at component-construction time so
// the first batch of preview widgets mount with demo on (Solid mounts children
// before the parent's `onMount`, so a `onMount` here would be too late).
export default function AddWidgetPage(props: AddWidgetPageProps) {
  setPreviewDemo(true);
  onCleanup(() => setPreviewDemo(false));

  const [pickerSearch, setPickerSearch] = createSignal('');
  const [groupBy, setGroupBy] = createSignal<GroupBy>('category');
  // Type of the widget whose preview is shown in the right-hand pane. Driven by
  // hover/focus; falls back to the first visible entry so the pane is never
  // blank while results exist.
  const [hoveredType, setHoveredType] = createSignal<string | null>(null);

  // Search index, rebuilt when the UI language changes (the registry's `label`
  // / `description` are `t()` getters, so reading them here subscribes this memo
  // to the language signal). Each widget is searchable by its TRANSLATED and its
  // English label/description, so an alias list written once in English keeps
  // working in a localized UI (WidgetPicker.SPEC [S-06]).
  const searchIndex = createMemo(() => {
    const enWidgets = en.widget as Record<string, { label?: string; description?: string } | undefined>;
    const idx = new Map<string, IndexedWidget>();
    for (const [type, def] of Object.entries(registry)) {
      const enEntry = enWidgets[type];
      idx.set(type, indexWidget({
        type,
        labels: [tOr(`widget.${type}.label`, def.label), enEntry?.label ?? ''].filter(Boolean),
        descriptions: [def.description ?? '', enEntry?.description ?? ''].filter(Boolean),
        keywords: WIDGET_KEYWORDS[type],
        categories: [def.category, tOr(`category.${def.category}`, '')].filter(Boolean),
        providers: def.providers,
      }));
    }
    return idx;
  });

  // Picker-visible entries. `devOnly` widgets are discoverable only in
  // developer mode; existing instances still render, only the picker hides
  // them. While filling a container, container types drop out too: they never
  // nest ([S-19]), so offering them would be a dead card ([S-34]).
  const visibleEntries = createMemo<[string, WidgetDefinition][]>(() => {
    const out: [string, WidgetDefinition][] = [];
    for (const [type, def] of Object.entries(registry)) {
      if (def.devOnly && !globalDesign.devMode) continue;
      if ((props.intoContainer || props.intoTray) && isContainerType(type)) continue;
      out.push([type, def]);
    }
    const q = pickerSearch().trim();
    // With a query the list is ranked best-match-first ([S-05]); without one it
    // is the alphabetical library.
    if (q) {
      const idx = searchIndex();
      return rankWidgets(
        out.flatMap(e => {
          const indexed = idx.get(e[0]);
          return indexed ? [{ indexed, value: e }] : [];
        }),
        q,
      );
    }
    out.sort(([, a], [, b]) => a.label.localeCompare(b.label));
    return out;
  });

  // Ranking and bucketing are mutually exclusive: a grouped list would scatter
  // the best match into whatever category header it happens to belong to, so a
  // search collapses to one flat, ranked list ([S-05]).
  const pickerGroups = createMemo(() =>
    pickerSearch().trim()
      ? computePickerGroups(visibleEntries(), 'none')
      : computePickerGroups(visibleEntries(), groupBy()));

  // Resolve the entry shown in the preview pane: the hovered one if it's still
  // visible under the current search, else the first visible entry, else null.
  const previewEntry = createMemo<[string, WidgetDefinition] | null>(() => {
    const entries = visibleEntries();
    if (!entries.length) return null;
    const ht = hoveredType();
    if (ht) {
      const found = entries.find(([type]) => type === ht);
      if (found) return found;
    }
    return entries[0];
  });

  const atWidgetLimit = createMemo(() => atFreeWidgetLimit());

  function handleKeyDown(e: KeyboardEvent) {
    // The Group-by dropdown owns its own keyboard nav (arrows/Enter). Its keydown
    // bubbles up to this container, so without this guard ArrowDown/Enter would
    // also move card focus / add a widget while the dropdown is open.
    if ((e.target as HTMLElement | null)?.closest('.picker-groupby')) return;
    const root = e.currentTarget as HTMLElement;
    const cards = Array.from(root.querySelectorAll<HTMLButtonElement>('button.widget-card'));
    if (!cards.length) return;
    const active = document.activeElement as HTMLElement | null;
    const onSearch = active?.classList.contains('picker-search');
    // From the search input: Enter activates the first card; ArrowDown moves
    // into the list. All other keys belong to the input.
    if (onSearch) {
      if (e.key === 'Enter') { e.preventDefault(); cards[0]?.click(); return; }
      if (e.key !== 'ArrowDown') return;
      e.preventDefault();
      cards[0]?.focus();
      return;
    }
    if (e.key === 'Enter' || e.key === ' ') {
      const idx = cards.indexOf(active as HTMLButtonElement);
      if (idx >= 0) { e.preventDefault(); cards[idx]?.click(); }
      return;
    }
    const idx = cards.indexOf(active as HTMLButtonElement);
    const next = nextIndexLinear(e.key, idx, cards.length, /*wrap*/ false);
    if (next !== null && next !== idx) { e.preventDefault(); cards[next]?.focus(); }
  }

  return (
    <div class="add-widget-page" onKeyDown={handleKeyDown}>
      <div class="picker-topbar">
        <button class="picker-back" onClick={() => props.onBack()} title={t('common.back')}>
          <ArrowLeft size={16} strokeWidth={1.5} />
          <span>{t('common.back')}</span>
        </button>
        {/* Filling a container ([S-34]): name the target, so a pick that lands
            inside the stack instead of on the desktop is never a surprise. */}
        <Show when={props.intoContainer}>
          <span class="picker-into">
            <Layers size={13} strokeWidth={1.5} />
            <span>{t('app.pickerInto', { name: props.intoContainer! })}</span>
          </span>
        </Show>
        {/* Adding to the tray (Tray.SPEC [S-01]): the pick gets an icon rather
            than a place on the desktop, so say so before it is made. */}
        <Show when={props.intoTray}>
          <span class="picker-into">
            <PanelBottom size={13} strokeWidth={1.5} />
            <span>{t('tray.pickerInto')}</span>
          </span>
        </Show>
        <div class="picker-search-wrap">
          <span class="picker-search-icon"><Search size={14} strokeWidth={1.5} /></span>
          <input
            class="picker-search"
            type="text"
            placeholder={t('app.pickerSearchPlaceholder')}
            value={pickerSearch()}
            onInput={e => setPickerSearch(e.target.value)}
            ref={el => Promise.resolve().then(() => el?.focus())}
          />
          <Show when={pickerSearch()}>
            <button class="picker-search-clear" onClick={() => setPickerSearch('')}><X size={14} strokeWidth={1.5} /></button>
          </Show>
        </div>
        <div class="picker-groupby">
          <span class="picker-groupby-label">{t('app.pickerGroupBy')}</span>
          <Dropdown
            value={groupBy()}
            options={groupByOptions()}
            onChange={(v) => setGroupBy(v as GroupBy)}
            class="picker-groupby-dropdown"
          />
        </div>
      </div>

      <div class="picker-body">
        <div class="picker-scroll cfg-scrollbar">
          <Show
            when={pickerGroups().length > 0}
            fallback={<div class="picker-empty">{t('app.pickerEmpty')}</div>}
          >
            <For each={pickerGroups()}>
              {({ heading, entries }) => (
                <div class="picker-section">
                  <Show when={heading}>
                    <div class="picker-category">{translateHeading(heading!, groupBy())}</div>
                  </Show>
                  <div class="picker-list">
                    <For each={entries}>
                      {([type, def]) => (
                        <button
                          class="widget-card kbd-focusable"
                          // Registry key on the card, so tooling can target a
                          // specific widget without matching on its translated
                          // label (the docs screenshot exporter walks these).
                          data-widget-type={type}
                          onClick={() => props.onPick(type, def)}
                          onMouseEnter={() => setHoveredType(type)}
                          onFocus={() => setHoveredType(type)}
                        >
                          <div class="widget-card-icon">{def.icon()}</div>
                          <div class="widget-card-text">
                            <div class="widget-card-label">{tOr(`widget.${type}.label`, def.label)}</div>
                            <Show when={def.description}>
                              <div class="widget-card-desc">{def.description}</div>
                            </Show>
                          </div>
                        </button>
                      )}
                    </For>
                  </div>
                </div>
              )}
            </For>
          </Show>
          <Show when={atWidgetLimit()}>
            <div class="picker-limit">
              <Crown size={13} strokeWidth={1.5} />
              <span>{t('app.pickerLimit')}</span>
            </div>
          </Show>
        </div>

        {/* Persistent preview pane, always visible, swaps to the hovered
            (or first visible) widget, rendered live with demo data. */}
        <div class="picker-preview-pane">
          <Show when={previewEntry()}>
            {(entry) => (
              <>
                <div class="picker-preview-frame">
                  <WidgetRowPreview type={entry()[0]} def={entry()[1]} />
                </div>
                <div class="picker-preview-meta">
                  <div class="picker-preview-label">
                    {tOr(`widget.${entry()[0]}.label`, entry()[1].label)}
                  </div>
                  <Show when={entry()[1].description}>
                    <div class="picker-preview-desc">{entry()[1].description}</div>
                  </Show>
                </div>
                <Show when={import.meta.env.DEV}>
                  <DevAspectPanel type={entry()[0]} fallback={spanFromSize(entry()[1].defaultSize)} />
                </Show>
              </>
            )}
          </Show>
        </div>
      </div>
    </div>
  );
}
