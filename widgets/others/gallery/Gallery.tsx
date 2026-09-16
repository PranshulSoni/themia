import { createSignal, createEffect, onMount, onCleanup, Show, For, createMemo } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoGalleryImages } from '../../../src/demo/fixtures.ts';
import { ChevronLeft, ChevronRight, Pause, Play, FolderOpen, ImageIcon, LayoutGrid, Shuffle, SquareStack, RefreshCw, Settings, X } from 'lucide-solid';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { registerWidgetKeyboard, type WidgetKeyEvent } from '../../../src/input/widgetKeyboard.ts';
import { galleryKeyAction } from './galleryKeys.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import { logger } from '../../../src/debugLog.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import './Gallery.css';

// Closed set, never a loose string (CLAUDE.md trust-boundary rule).
type GallerySource = 'folder' | 'file' | 'bing';

interface GalleryConfigData {
  source: GallerySource;
  path: string;
  filePath: string;
  recursive: boolean;
  showCaption: boolean;
  mode: 'grid' | 'slideshow' | 'scatter' | 'mosaic';
  fit: 'cover' | 'contain';
  maxImages: number;
  slideshowInterval: number;
  refreshInterval: number;
  refreshNonce: number;
}

// Bing's "Photo of the Day" metadata endpoint (same one the standalone
// Photo-of-the-Day widget uses). Fetched server-side via `http_get` to dodge CORS.
const BING_API = 'https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-US';

interface GalleryProps extends GalleryConfigData {
  _widgetId?: string;
  _onConfigure?: () => void;
  label?: string;
  headerMode?: HeaderMode;
}

interface ImageItem {
  url: string;
  openAction: string; // file path ('' = remote/demo, not openable)
  title?: string;     // caption (Bing source)
  credit?: string;    // caption (Bing source)
}

function pickRandom<T>(arr: T[], count: number): T[] {
  if (count <= 0 || count >= arr.length) return [...arr];
  const pool = [...arr];
  const result: T[] = [];
  for (let i = 0; i < count; i++) {
    const j = Math.floor(Math.random() * pool.length);
    result.push(pool[j]);
    pool[j] = pool[pool.length - 1];
    pool.pop();
  }
  return result;
}

interface ScatterItem {
  img: ImageItem;
  x: number;
  y: number;
  size: number;
  rotation: number;
  z: number;
}

function generateScatter(images: ImageItem[]): ScatterItem[] {
  return images.map((img, i) => {
    const angle = Math.random() * Math.PI * 2;
    const radius = Math.random() * 0.28;
    const x = 50 + Math.cos(angle) * radius * 100;
    const y = 50 + Math.sin(angle) * radius * 100;
    const size = 25 + Math.random() * 20;
    const rotation = (Math.random() - 0.5) * 24;
    return { img, x, y, size, rotation, z: i };
  });
}

interface MosaicCell {
  img: ImageItem;
  x: number;
  y: number;
  w: number;
  h: number;
}

function generateMosaic(images: ImageItem[]): MosaicCell[] {
  if (images.length === 0) return [];
  const cells: MosaicCell[] = [];

  function subdivide(
    items: ImageItem[],
    x: number, y: number, w: number, h: number,
    vertical: boolean,
  ) {
    if (items.length === 1) {
      cells.push({ img: items[0], x, y, w, h });
      return;
    }
    const split = Math.floor(items.length / 2);
    const ratio = 0.35 + Math.random() * 0.3;
    const a = items.slice(0, split);
    const b = items.slice(split);

    if (vertical) {
      const wA = w * ratio;
      subdivide(a, x, y, wA, h, !vertical);
      subdivide(b, x + wA, y, w - wA, h, !vertical);
    } else {
      const hA = h * ratio;
      subdivide(a, x, y, w, hA, !vertical);
      subdivide(b, x, y + hA, w, h - hA, !vertical);
    }
  }

  subdivide(images, 0, 0, 100, 100, images.length > 1 && Math.random() > 0.5);
  return cells;
}

// ── Sequential loading queue (max 2 concurrent) ─────────────────────────────

const imgQueue: Array<() => Promise<void>> = [];
let imgActive = 0;
const IMG_CONCURRENCY = 2;

function enqueueImageLoad(task: () => Promise<void>) {
  imgQueue.push(task);
  drainImageQueue();
}

function drainImageQueue() {
  while (imgActive < IMG_CONCURRENCY && imgQueue.length > 0) {
    const task = imgQueue.shift()!;
    imgActive++;
    task().finally(() => { imgActive--; drainImageQueue(); });
  }
}

function AsyncImage(p: { src: string; path: string; fit: string }) {
  const [dataUrl, setDataUrl] = createSignal('');
  const [loaded, setLoaded] = createSignal(false);
  // Which image is currently applied. Re-setting the SAME src does not re-fire
  // the <img> onLoad, so resetting `loaded` on every effect run (e.g. a refresh
  // that yields the identical URL) would strand opacity at 0 forever. Only reset
  // the fade when the image actually changes.
  let appliedKey = '';

  // React to src/path changes so a reused <img> (e.g. the slideshow advancing
  // through one element) actually swaps images, not just the first mount.
  createEffect(() => {
    const src = p.src;
    const path = p.path;
    const key = src || path;
    if (key === appliedKey) return;   // same image — keep it visible, don't re-fade
    appliedKey = key;
    setLoaded(false);
    if (src) { setDataUrl(src); return; }   // direct URL (remote / demo)
    setDataUrl('');
    if (!path) return;
    enqueueImageLoad(() =>
      invoke('read_image_base64', { path })
        .then((url) => { setDataUrl(url); })
        .catch((e) => logger.error('gallery', `read_image_base64 failed: ${e}`))
    );
  });

  return (
    <img
      src={dataUrl()}
      alt=""
      style={`object-fit:${p.fit};opacity:${loaded() ? 1 : 0}`}
      class="gallery-async-img"
      onLoad={() => setLoaded(true)}
    />
  );
}

export default function Gallery(props: GalleryProps) {
  const layoutStore = useLayout();
  const [allImages, setAllImages] = createSignal<ImageItem[]>([]);
  const [cycle, setCycle] = createSignal(0);
  const [currentIndex, setCurrentIndex] = createSignal(0);
  const [loading, setLoading] = createSignal(false);

  const displayImages = createMemo(() => {
    void cycle();
    const imgs = allImages();
    const max = Math.min(Math.max(1, props.maxImages ?? 12), 20);
    if (imgs.length > max) return pickRandom(imgs, max);
    return [...imgs];
  });

  // Keep the slideshow index in range when displayImages() shrinks (e.g. lowering
  // "Max images", which re-subsets the set): a stale out-of-range index blanks the
  // slide, shows a wrong `N / total` counter [S-08], and throws on click via
  // openImage(undefined). Event-driven, no wall-clock timing (CLAUDE.md).
  createEffect(() => {
    const len = displayImages().length;
    if (len > 0 && currentIndex() >= len) setCurrentIndex(len - 1);
  });

  const scatterItems = createMemo(() => {
    if (props.mode !== 'scatter') return [];
    return generateScatter(displayImages());
  });

  const mosaicCells = createMemo(() => {
    if (props.mode !== 'mosaic') return [];
    return generateMosaic(displayImages());
  });

  const source = (): GallerySource => props.source ?? 'folder';
  const isSingle = () => source() === 'file' || source() === 'bing';

  // Request-id guard: each load path bumps this before awaiting; a slower
  // in-flight scan_images/http_get for a previous folder/source then bails
  // instead of clobbering the current images (#64). Mirrors the `seq` guard in
  // todo/stocks.
  let loadSeq = 0;

  async function loadFolder() {
    const my = ++loadSeq;
    if (isDemo()) {
      setAllImages(demoGalleryImages().map(url => ({ url, openAction: '' })));
      setCurrentIndex(0);
      setLoading(false);
      return;
    }
    if (!props.path) { setAllImages([]); return; }
    setLoading(true);
    try {
      const result = await invoke('scan_images', {
        path: props.path,
        recursive: props.recursive,
      });
      if (my !== loadSeq) return;
      setAllImages(result.map(p => ({ url: '', openAction: p })));
      setCurrentIndex(0);
    } catch {
      if (my !== loadSeq) return;
      setAllImages([]);
    }
    if (my !== loadSeq) return;
    setLoading(false);
  }

  function loadFile() {
    // Bump so any in-flight async load (folder/bing) becomes stale and bails
    // instead of overwriting this file's image (#64).
    ++loadSeq;
    if (isDemo()) {
      setAllImages([{ url: demoGalleryImages()[0], openAction: '' }]);
      setCurrentIndex(0);
      setLoading(false);
      return;
    }
    setAllImages(props.filePath ? [{ url: '', openAction: props.filePath }] : []);
    setCurrentIndex(0);
    setLoading(false);
  }

  async function loadBing() {
    const my = ++loadSeq;
    setLoading(true);
    if (isDemo()) {
      setAllImages([{ url: demoGalleryImages()[0], openAction: '', title: 'Demo Photo of the Day', credit: '© Themia' }]);
      setCurrentIndex(0);
      setLoading(false);
      return;
    }
    try {
      const raw = await invoke('http_get', { url: BING_API });
      if (my !== loadSeq) return;
      const data = JSON.parse(raw) as { images?: { url?: string; title?: string; copyright?: string }[] };
      const first = data.images?.[0];
      if (!first?.url) { setAllImages([]); setLoading(false); return; }
      setAllImages([{
        url: `https://www.bing.com${first.url}`,
        openAction: '',
        title: first.title ?? '',
        credit: first.copyright ?? '',
      }]);
      setCurrentIndex(0);
    } catch (e) {
      if (my !== loadSeq) return;
      logger.error('gallery', `Bing photo fetch failed: ${e}`);
      setAllImages([]);
    }
    if (my !== loadSeq) return;
    setLoading(false);
  }

  function load() {
    switch (source()) {
      case 'file': loadFile(); break;
      case 'bing': loadBing(); break;
      default:     loadFolder(); break;
    }
  }

  function refresh() {
    load();
    setCycle(c => c + 1);
  }

  function openImage(item: ImageItem) {
    if (item.openAction) {
      invoke('open_file', { path: item.openAction }).catch((e) => logger.error('gallery', `open_file failed: ${e}`));
    }
  }

  function handleImageRightClick(e: MouseEvent, item: ImageItem) {
    const wid = props._widgetId;
    function setWidgetCfg(partial: Partial<GalleryConfigData>): void {
      if (!wid) return;
      // updateWidgetConfig REPLACES the stored config (layout.ts), so spread the
      // full existing config rather than a key whitelist — otherwise headerMode/
      // label (and any future key) are dropped. Mirrors FolderView's setWidgetCfg.
      const w = layoutStore.layout.widgets.find((x) => x.id === wid);
      const cfg = (w?.config ?? {}) as Record<string, unknown>;
      layoutStore.updateWidgetConfig(wid, { ...cfg, ...partial });
    }
    showCtxMenu(e, [
      ...(item.openAction ? [
        {
          icon: ImageIcon,
          label: t('common.open'),
          action: () => invoke('open_file', { path: item.openAction }).catch((e) => logger.error('gallery', `open_file failed: ${e}`)),
        },
        {
          icon: FolderOpen,
          label: t('gallery.ctxShowInExplorer'),
          action: () => invoke('open_in_explorer', { path: item.openAction }).catch((e) => logger.error('gallery', `open_in_explorer failed: ${e}`)),
        },
        { sep: true },
      ] : []),
      ...(wid ? [
        // Layout switching only applies to a multi-image folder source.
        ...(source() === 'folder' ? [
          { header: true, label: t('ctxMenu.view') },
          { icon: LayoutGrid, label: t('ctxMenu.grid'), active: (props.mode ?? 'grid') === 'grid',      action: () => setWidgetCfg({ mode: 'grid' }) },
          { icon: Play,       label: t('gallery.modeSlideshow'), active: props.mode === 'slideshow',              action: () => setWidgetCfg({ mode: 'slideshow' }) },
          { icon: Shuffle,    label: t('gallery.modeScatter'), active: props.mode === 'scatter',                action: () => setWidgetCfg({ mode: 'scatter' }) },
          { icon: SquareStack,label: t('gallery.modeMosaic'), active: props.mode === 'mosaic',                 action: () => setWidgetCfg({ mode: 'mosaic' }) },
          { sep: true },
        ] : []),
        { icon: RefreshCw,  label: t('gallery.ctxRefresh'), action: () => setWidgetCfg({ refreshNonce: Date.now() }) },
        { sep: true },
        { icon: Settings, label: t('ctxMenu.configure'), action: () => props._onConfigure?.() },
        { icon: X, label: t('ctxMenu.removeWidget'), danger: true, action: async () => { if (window.confirm(t('ctxMenu.removeWidgetConfirm'))) layoutStore.removeWidget(wid!); } },
      ] : []),
    ]);
  }

  const isConfigured = () => {
    if (isDemo()) return true;
    switch (source()) {
      case 'bing': return true;            // no per-widget setup needed
      case 'file': return !!props.filePath;
      default:     return !!props.path;
    }
  };

  // Reload images when source config changes; periodic refresh
  createEffect(() => {
    void props.source; void props.path; void props.filePath; void props.recursive; void props.refreshNonce;
    refresh();
  });

  createPolling(refresh, () => Math.max(1, props.refreshInterval ?? 60) * 60_000, 'gallery');

  // Slideshow timer
  const [paused, setPaused] = createSignal(false);

  createPolling(() => {
    const imgs = displayImages();
    if (props.mode !== 'slideshow' || imgs.length <= 1 || paused()) return;
    setCurrentIndex(i => (i + 1) % imgs.length);
  }, () => Math.max(1, props.slideshowInterval ?? 10) * 1000);

  function prev() {
    const len = displayImages().length;
    if (len === 0) return;
    setCurrentIndex(i => (i - 1 + len) % len);
  }
  function next() {
    const len = displayImages().length;
    if (len === 0) return;
    setCurrentIndex(i => (i + 1) % len);
  }

  // Slideshow keyboard ([S-29]/[S-37]). Routed through the shared widget-keyboard
  // hook (like Music), NOT a global `document` listener: widgets live in
  // WS_EX_NOACTIVATE windows, so a document keydown only ever fires while a text
  // input elsewhere in the same window (notes/todo) is focused, and the old
  // listener called preventDefault() on Space there, eating the space out of
  // that input (themia-releases #53). The Rust hook stands down entirely while a
  // text input is focused, and `galleryKeyAction` guards on it too, so typing is
  // never disturbed. Keys only arrive after the gallery is clicked (activate).
  function handleKey(e: WidgetKeyEvent): void {
    const tag = document.activeElement?.tagName;
    const typing = tag === 'INPUT' || tag === 'TEXTAREA';
    const action = galleryKeyAction(e.key, props.mode, typing);
    if (!action) return;
    if (action === 'prev') prev();
    else if (action === 'next') next();
    else setPaused(p => !p);
  }

  let _kbd: ReturnType<typeof registerWidgetKeyboard> | undefined;
  onMount(() => { _kbd = registerWidgetKeyboard(handleKey); });
  onCleanup(() => _kbd?.dispose());
  function activateKbd(e: MouseEvent): void { if (e.button === 0) _kbd?.activate(); }

  return (
    <div class="gallery-widget" onMouseDown={activateKbd}>
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={ImageIcon}
        title={props.label || 'Gallery'}
      />
      <div class="widget-body gallery-body">
      <Show when={!isConfigured()}>
        <div class="gallery-empty">{source() === 'file' ? t('widgetUi.galleryPickImage') : t('widgetUi.galleryPickFolder')}<br/>{t('widgetUi.clickToConfigure')} <Settings size={13} strokeWidth={1.5} style={{ 'vertical-align': '-2px' }} /> {t('widgetUi.toConfigure')}</div>
      </Show>

      <Show when={isConfigured() && loading() && allImages().length === 0}>
        <div class="gallery-empty">{t('widgetUi.loading')}</div>
      </Show>

      <Show when={isConfigured() && !loading() && allImages().length === 0}>
        <div class="gallery-empty">{source() === 'bing' ? 'Could not load photo' : source() === 'file' ? 'Image not found' : 'No images found'}</div>
      </Show>

      {/* Single image (file / photo-of-the-day source) */}
      <Show when={isSingle() && displayImages().length > 0}>
        {(() => {
          const item = () => displayImages()[0];
          return (
            <div class="gallery-single" onClick={() => openImage(item())} onContextMenu={(e) => handleImageRightClick(e, item())}>
              <AsyncImage src={item().url} path={item().openAction} fit={props.fit} />
              <Show when={source() === 'bing' && (props.showCaption ?? true) && (item().title || item().credit)}>
                <div class="gallery-caption">
                  <Show when={item().title}><span class="gallery-caption-title">{item().title}</span></Show>
                  <Show when={item().credit}><span class="gallery-caption-credit">{item().credit}</span></Show>
                </div>
              </Show>
            </div>
          );
        })()}
      </Show>

      {/* Grid */}
      <Show when={!isSingle() && props.mode === 'grid' && displayImages().length > 0}>
        <div class="gallery-grid">
          <For each={displayImages()}>
            {(item) => (
              <div class="gallery-thumb" onClick={() => openImage(item)} onContextMenu={(e) => handleImageRightClick(e, item)}>
                <AsyncImage src={item.url} path={item.openAction} fit={props.fit} />
              </div>
            )}
          </For>
        </div>
      </Show>

      {/* Slideshow */}
      <Show when={!isSingle() && props.mode === 'slideshow' && displayImages().length > 0}>
        <div class="gallery-slideshow">
          <div
            class="gallery-slideshow__img-wrap"
            onClick={() => openImage(displayImages()[currentIndex()])}
            onContextMenu={(e) => handleImageRightClick(e, displayImages()[currentIndex()])}
          >
            <AsyncImage
              src={displayImages()[currentIndex()]?.url}
              path={displayImages()[currentIndex()]?.openAction}
              fit={props.fit}
            />
          </div>
          <div class="slideshow-controls">
            <button class="ss-btn" onClick={prev}>
              <ChevronLeft size={16} strokeWidth={1.5} />
            </button>
            <button class="ss-btn" onClick={() => setPaused(p => !p)}>
              <Show when={paused()} fallback={<Pause size={14} strokeWidth={1.5} />}>
                <Play size={14} strokeWidth={1.5} />
              </Show>
            </button>
            <button class="ss-btn" onClick={next}>
              <ChevronRight size={16} strokeWidth={1.5} />
            </button>
            <span class="ss-counter">{currentIndex() + 1} / {displayImages().length}</span>
          </div>
        </div>
      </Show>

      {/* Scatter */}
      <Show when={!isSingle() && props.mode === 'scatter' && scatterItems().length > 0}>
        <div class="gallery-scatter">
          <For each={scatterItems()}>
            {(item) => (
              <div
                class="scatter-photo"
                style={{
                  left: `${item.x}%`,
                  top: `${item.y}%`,
                  width: `${item.size}%`,
                  transform: `translate(-50%, -50%) rotate(${item.rotation}deg)`,
                  "z-index": item.z,
                }}
                onClick={() => openImage(item.img)}
                onContextMenu={(e) => handleImageRightClick(e, item.img)}
              >
                <AsyncImage src={item.img.url} path={item.img.openAction} fit={props.fit} />
              </div>
            )}
          </For>
        </div>
      </Show>

      {/* Mosaic */}
      <Show when={!isSingle() && props.mode === 'mosaic' && mosaicCells().length > 0}>
        <div class="gallery-mosaic">
          <div class="gallery-mosaic__inner">
            <For each={mosaicCells()}>
              {(cell) => (
                <div
                  class="mosaic-cell"
                  style={{
                    left: `${cell.x}%`,
                    top: `${cell.y}%`,
                    width: `${cell.w}%`,
                    height: `${cell.h}%`,
                  }}
                  onClick={() => openImage(cell.img)}
                  onContextMenu={(e) => handleImageRightClick(e, cell.img)}
                >
                  {/* Mosaic tiles fill their computed cells, always cover, regardless of the fit config. */}
                  <AsyncImage src={cell.img.url} path={cell.img.openAction} fit="cover" />
                </div>
              )}
            </For>
          </div>
        </div>
      </Show>
      </div>
    </div>
  );
}
