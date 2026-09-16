// ---------------------------------------------------------------------------
// Connect account window (mounted from main.tsx on `?connect=1`)
//
// A focused, guided wizard for adding a connection in its own OS window:
//   pick provider → enter details / run OAuth → verify → success | troubleshoot
//
// Every backend add/auth path connects to validate before persisting, so a
// successful step *is* the connection test. On success we broadcast
// `accounts-changed` (sync.ts → other windows refresh) and close.
// ---------------------------------------------------------------------------

import { createSignal, createMemo, createEffect, onCleanup, onMount, Show, Switch, Match, For } from 'solid-js';
import { createStore } from 'solid-js/store';
import type { JSX } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { listen } from '@tauri-apps/api/event';
import { Check, Copy, ArrowLeft, Loader, ShieldCheck, CircleAlert, ExternalLink, KeyRound, TriangleAlert, Pencil } from 'lucide-solid';
import {
  CONNECT_PROVIDERS, findProvider,
  googleAuth, claudeStart, claudeComplete, openExternal,
  type ConnectProvider, type ConnectField, type DeviceAuth,
} from './connect/providers.tsx';
import { visibleProviders } from './connect/visibility.ts';
import {
  presetsFor, applyPreset, matchPresetByEmail, remainingPlaceholders, presetOwnedFields,
  isPresetProtocol, type ConnectPreset, type PresetProtocol,
} from './connect/presets.ts';
import { probePreset, healthMessage, clearProbeCache, type PresetHealth } from './connect/probe.ts';
import { troubleshoot, type Troubleshooting } from './connect/troubleshoot.ts';
import { ProviderLogo } from './components/AccountManager.tsx';
import { BrandMark } from './components/BrandMark.tsx';
import { globalDesign } from './design.ts';
import { postTransient } from './sync.ts';
// App.css supplies the base font (`html { font-family: 'Segoe UI Variable'… }`)
// and the `body.config-window` UI scaling, same as the config window.
import './App.css';
import './styles/config-base.css';
import './components/AccountManager.css';
import './ConnectWindow.css';
import { t } from './i18n.ts';

// `preset` sits between picking a protocol and filling its form: choose your
// mail/calendar provider, THEN see the fields. One question per page, and the
// second page is already answered by the time you reach it. [S-47]
type Step = 'pick' | 'preset' | 'form' | 'connecting' | 'device' | 'paste' | 'success' | 'error';

const params = new URLSearchParams(window.location.search);
const initialProviderKey = params.get('provider');

function closeWindow(): void {
  getCurrentWindow().close().catch(() => {});
}

// ── Labeled form field ───────────────────────────────────────────────────────

function Field(props: { field: ConnectField; value: string; onInput: (v: string) => void }): JSX.Element {
  const f = props.field;
  const cls = f.type === 'number' ? 'num-input-sm' : 'text-input';
  return (
    <div class="field-col am-field">
      <label class="am-field-label">
        {f.label}
        <Show when={f.optional}><span class="optional">optional</span></Show>
      </label>
      <input
        class={cls}
        type={f.type}
        placeholder={f.placeholder}
        value={props.value}
        onInput={e => props.onInput(e.currentTarget.value)}
      />
      <Show when={f.hint}><div class="am-field-hint">{f.hint}</div></Show>
    </div>
  );
}

export default function ConnectWindow(): JSX.Element {
  const [step, setStep] = createSignal<Step>('pick');
  const [provider, setProvider] = createSignal<ConnectProvider | null>(null);
  const [values, setValues] = createStore<Record<string, string>>({});
  const [errorText, setErrorText] = createSignal<string>('');
  const [tips, setTips] = createSignal<Troubleshooting | null>(null);
  const [deviceAuth, setDeviceAuth] = createSignal<DeviceAuth | null>(null);
  const [copied, setCopied] = createSignal(false);
  const [resultEmail, setResultEmail] = createSignal<string | null>(null);
  // Claude manual-paste OAuth: the state issued by start, and the pasted code.
  const [claudeState, setClaudeState] = createSignal<string>('');
  const [pasteCode, setPasteCode] = createSignal<string>('');

  // ── Presets ([S-47]) ──
  // The chosen preset, its live health badge, and whether the user has hand-
  // edited a server field. That last flag is what stops address auto-detection
  // from overwriting settings someone deliberately typed.
  const [preset, setPreset] = createSignal<ConnectPreset | null>(null);
  const [health, setHealth] = createSignal<PresetHealth>({ status: 'unknown' });
  const [serverEdited, setServerEdited] = createSignal(false);
  // Guards against a slow probe for a preset the user already moved on from
  // landing on the badge of the one they picked next.
  let probeSeq = 0;

  let pollTimer: ReturnType<typeof setInterval> | null = null;
  const stopPoll = () => { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } };
  onCleanup(stopPoll);
  onCleanup(clearProbeCache);

  const pickable = createMemo(() => visibleProviders(CONNECT_PROVIDERS, globalDesign.devMode));
  const services  = createMemo(() => pickable().filter(p => p.group === 'service'));
  const protocols = createMemo(() => pickable().filter(p => p.group === 'protocol'));

  function succeed(email?: string | null): void {
    stopPoll();
    setResultEmail(email ?? null);
    setStep('success');
    // Tell every other window to reload its account list, then close shortly.
    postTransient({ type: 'accounts-changed' });
    setTimeout(closeWindow, 1400);
  }

  function fail(err: unknown): void {
    stopPoll();
    const msg = String(err);
    setErrorText(msg);
    setTips(troubleshoot(provider()?.key ?? '', msg));
    setStep('error');
  }

  /**
   * Drive any device-code provider ([S-51]). One path, parameterised by the
   * provider's own `device` pair, rather than a branch per provider: Microsoft
   * and GitHub run the identical grant, and a second copy of the polling loop
   * is a second place for the interval or the stop-on-error to drift.
   */
  async function beginDevice(p: ConnectProvider): Promise<void> {
    if (!p.device) { fail(`${p.label} has no device flow configured.`); return; }
    setStep('connecting');
    try {
      const auth = await p.device.start();
      setDeviceAuth(auth);
      setStep('device');
      pollTimer = setInterval(async () => {
        try {
          const id = await p.device!.poll(auth.device_code);
          if (id !== null) succeed();
        } catch (e) { fail(e); }
        // +1s over the server's stated interval: both providers reject polling
        // faster than they asked for (`slow_down`).
      }, (Number(auth.interval) + 1) * 1000);
    } catch (e) { fail(e); }
  }

  async function beginGoogle(): Promise<void> {
    setStep('connecting');
    try {
      await googleAuth();
      succeed();
    } catch (e) { fail(e); }
  }

  // Claude: open the consent page, then land on the paste step where the user
  // enters the `code#state` the callback showed. `complete` verifies + saves.
  async function beginClaude(): Promise<void> {
    setPasteCode('');
    setErrorText('');
    try {
      const state = await claudeStart();
      setClaudeState(state);
      setStep('paste');
    } catch (e) { fail(e); }
  }

  async function submitClaude(): Promise<void> {
    if (!pasteCode().trim()) return;
    setErrorText('');
    setStep('connecting');
    try {
      const res = await claudeComplete(pasteCode(), claudeState());
      succeed(res?.email ?? null);
    } catch (e) { fail(e); }
  }

  function selectProvider(p: ConnectProvider): void {
    setProvider(p);
    setErrorText('');
    setTips(null);
    setPreset(null);
    setHealth({ status: 'unknown' });
    setServerEdited(false);
    if (p.kind === 'form') {
      const seed: Record<string, string> = {};
      for (const f of p.fields ?? []) seed[f.name] = f.default ?? '';
      setValues(seed);
      // A protocol asks "who is your provider?" before it asks for a server
      // address; a provider without presets (GitHub token, RSS) has only the
      // one question, so it goes straight to the form. [S-47]
      setStep(isPresetProtocol(p.key) ? 'preset' : 'form');
    } else if (p.kind === 'oauth-device') {
      beginDevice(p);
    } else if (p.kind === 'oauth-paste') {
      beginClaude();
    } else {
      beginGoogle();
    }
  }

  // ── Presets ───────────────────────────────────────────────────────────────

  /** The protocol whose presets apply here, or null for a provider that has
   *  none (GitHub's token form, RSS's feed URL). */
  const presetProtocol = createMemo<PresetProtocol | null>(() => {
    const key = provider()?.key;
    return key && isPresetProtocol(key) ? key : null;
  });

  const availablePresets = createMemo<ConnectPreset[]>(() => {
    const proto = presetProtocol();
    return proto ? presetsFor(proto) : [];
  });

  /**
   * Fill the server fields from a preset and check, on this machine, that the
   * endpoint still answers — the check that lets a stale preset be badged
   * instead of silently wasting the user's time ([S-47]).
   *
   * `auto` marks a selection made by address detection rather than a click; it
   * changes nothing about the fill, only that it hasn't overridden a hand edit.
   */
  function choosePreset(p: ConnectPreset, auto = false): void {
    setPreset(p);
    setValues(applyPreset(p, { ...values }));
    if (!auto) setServerEdited(false);
    setErrorText('');
    // A click on the preset page answers that page's question, so move on. An
    // auto-match from a typed address happens while already on the form.
    if (!auto) setStep('form');

    if (p.probe.kind === 'none') { setHealth({ status: 'unknown' }); return; }
    const seq = ++probeSeq;
    setHealth({ status: 'checking' });
    // Advisory: probePreset never rejects, and a bad result never blocks
    // Connect — `submit` is still the authority on whether this works.
    void probePreset(p).then(h => { if (seq === probeSeq) setHealth(h); });
  }

  /** Let go of the preset and hand the server fields to the user. Marking them
   *  edited also switches off address auto-detection, which is what makes the
   *  release stick instead of the next keystroke undoing it. [S-47] */
  function releasePreset(): void {
    probeSeq++;
    setPreset(null);
    setHealth({ status: 'unknown' });
    setServerEdited(true);
  }

  /** "Enter it myself" on the preset page: a blank form, nothing pre-filled,
   *  and no auto-detection — someone who declined the list should not then have
   *  the list fill their server field behind their back. */
  function enterManually(): void {
    releasePreset();
    // Wipe whatever a previously chosen preset left in the server fields.
    // Reaching this after picking Gmail and going back would otherwise land on
    // a form still holding imap.gmail.com with the Gmail label removed — the
    // one state more confusing than either a preset or a blank form. Only the
    // preset-owned fields reset; a typed username or password survives.
    const proto = presetProtocol();
    const p = provider();
    if (proto && p) {
      const owned = new Set(presetOwnedFields(proto));
      for (const f of p.fields ?? []) {
        if (owned.has(f.name)) setValues(f.name, f.default ?? '');
      }
    }
    setErrorText('');
    setStep('form');
  }

  /** Back to the provider list from the form, keeping what was typed. */
  function backToPresets(): void {
    probeSeq++;
    setErrorText('');
    setStep('preset');
  }

  /**
   * Field input. Two extras on top of storing the value:
   *  - typing a server field by hand pins it, so detection stops touching it;
   *  - typing an address detects the provider, which is the whole point —
   *    "me@gmail.com" fills in imap.gmail.com so nobody has to know it.
   */
  function onFieldInput(field: ConnectField, next: string): void {
    setValues(field.name, next);

    const proto = presetProtocol();
    if (!proto) return;

    if (presetOwnedFields(proto).includes(field.name)) {
      setServerEdited(true);
      // The fields no longer match the preset that filled them, so its badge
      // would be describing a server the user just replaced.
      if (preset()) releasePreset();
      return;
    }

    if (field.name === 'username' && !preset() && !serverEdited()) {
      const hit = matchPresetByEmail(proto, next);
      if (hit) choosePreset(hit, true);
    }
  }

  /** `{tokens}` a self-hosted preset left behind for the user to replace. */
  const pendingPlaceholders = createMemo(() => remainingPlaceholders({ ...values }));

  /** Fields grouped into rows: a field marked `inline` joins the one before it
   *  instead of starting its own row (IMAP server + port). */
  const fieldRows = createMemo<ConnectField[][]>(() => {
    const rows: ConnectField[][] = [];
    for (const f of provider()?.fields ?? []) {
      if (f.inline && rows.length > 0) rows[rows.length - 1].push(f);
      else rows.push([f]);
    }
    return rows;
  });

  /** The row holding the server settings — where the reachability check belongs,
   *  since that is the row it is a verdict on. -1 when the provider has none. */
  const serverRowIndex = createMemo(() => {
    const proto = presetProtocol();
    if (!proto) return -1;
    const owned = new Set(presetOwnedFields(proto));
    return fieldRows().findIndex(row => row.some(f => owned.has(f.name)));
  });

  const healthText = createMemo(() => {
    const p = preset();
    return p ? healthMessage(p, health()) : '';
  });

  function missingRequired(): boolean {
    const p = provider();
    if (!p?.fields) return false;
    return p.fields.some(f => !f.optional && !(values[f.name] ?? '').trim());
  }

  /** Connect stays disabled while a field still holds a template token: only
   *  the user knows their own server address, and submitting `{your-server}`
   *  produces a baffling DNS error instead of a clear instruction. */
  const cannotSubmit = () => missingRequired() || pendingPlaceholders().length > 0;

  async function submitForm(): Promise<void> {
    const p = provider();
    if (!p?.submit) return;
    if (missingRequired()) { setErrorText('Fill in all required fields.'); return; }
    if (pendingPlaceholders().length > 0) {
      setErrorText(`Replace ${pendingPlaceholders().join(' and ')} with your own server address.`);
      return;
    }
    setErrorText('');
    setStep('connecting');
    try {
      const res = await p.submit({ ...values });
      const email = (res && typeof res === 'object' && 'email' in res) ? String((res as { email: unknown }).email) : null;
      succeed(email);
    } catch (e) { fail(e); }
  }

  function retry(): void {
    const p = provider();
    if (!p) { setStep('pick'); return; }
    if (p.kind === 'form') setStep('form');
    else if (p.kind === 'oauth-device') beginDevice(p);
    else if (p.kind === 'oauth-paste') beginClaude();
    else beginGoogle();
  }

  /** One step back, not all the way out: a protocol's form returns to its
   *  provider list, and only the list returns to the picker. [S-47] */
  function back(): void {
    stopPoll();
    setDeviceAuth(null);
    setErrorText('');
    if (step() === 'form' && availablePresets().length > 0) { backToPresets(); return; }
    setProvider(null);
    setStep('pick');
  }

  function copyCode(code: string): void {
    navigator.clipboard.writeText(code).catch(() => {});
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  onMount(() => {
    const p = findProvider(initialProviderKey);
    // Respect the pre-release gate: a hidden (devOnly) provider isn't openable
    // via a ?provider= deep-link either, unless developer mode is on.
    if (p && pickable().includes(p)) selectProvider(p);

    // This window is a singleton: clicking another provider on the Connections
    // page re-invokes open_connect_window, which focuses this window and emits
    // connect-navigate. Re-select so the picker switches instead of stranding
    // the user on the previously chosen provider.
    const unlisten = listen<{ provider?: string | null }>('connect-navigate', (e) => {
      const np = findProvider(e.payload?.provider ?? null);
      if (np && pickable().includes(np)) selectProvider(np);
      else { setProvider(null); setStep('pick'); }
    });
    onCleanup(() => { unlisten.then(fn => fn()).catch(() => {}); });
  });

  const heading = createMemo(() => {
    switch (step()) {
      case 'pick':       return 'Add a connection';
      case 'preset':     return `Connect ${provider()?.label ?? ''}`;
      case 'form':       return `Connect ${preset()?.label ?? provider()?.label ?? ''}`;
      case 'connecting': return 'Connecting…';
      case 'device':     return 'Finish signing in';
      case 'paste':      return 'Finish signing in';
      case 'success':    return 'Connected';
      case 'error':      return 'Couldn’t connect';
    }
  });

  // The window is decorated (`decorations(true)` on CONNECT_WINDOW), so the OS
  // title bar IS this window's header — an in-app one duplicated its title and
  // close button. The step heading goes to the real title bar instead. [S-50]
  createEffect(() => {
    const title = heading() ?? 'Connect account';
    try {
      getCurrentWindow().setTitle(title).catch(() => {
        // ignore: the title is cosmetic; a rejected set must not break the step.
      });
    } catch {
      // ignore: no Tauri window at all (browser harness). `getCurrentWindow`
      // throws synchronously there, and an effect that throws tears down the
      // render pass — which silently stranded a `?provider=` deep-link on the
      // picker instead of its preset page.
    }
  });

  // Back is offered whenever there is a step to go back TO. A deep-linked
  // provider (`?provider=`) has no picker behind it, but its form still has its
  // own preset page, so that case is judged per-step rather than globally.
  const canGoBack = () => {
    const s = step();
    if (s === 'success' || s === 'pick') return false;
    if (s === 'form' && availablePresets().length > 0) return true;
    return !initialProviderKey && pickable().length > 1;
  };

  return (
    <div class="connect-window">
      <div class="connect-body cfg">
        {/* Back is the one control the OS title bar can't provide, so it moves
            inline rather than justifying a second header. */}
        <Show when={canGoBack()}>
          <button class="connect-back" onClick={back}>
            <ArrowLeft size={14} strokeWidth={1.5} /> {t('common.back')}
          </button>
        </Show>

        <Switch>
          {/* ── Provider picker ── */}
          <Match when={step() === 'pick'}>
            <p class="connect-lede">
              Pick a service to connect. For sign-in providers you’ll be sent to their page.
              We never see your password. Credentials are stored locally.
            </p>

            <Show when={services().length > 0}>
              <div class="connect-group">
                <div class="connect-group-label">{t('account.servicesGroup')}</div>
                <div class="connect-provider-grid">
                  <For each={services()}>{p => <ProviderButton p={p} onPick={() => selectProvider(p)} />}</For>
                </div>
              </div>
            </Show>

            <Show when={protocols().length > 0}>
              <div class="connect-group">
                <div class="connect-group-label">{t('account.protocolsGroup')}</div>
                <div class="connect-provider-grid">
                  <For each={protocols()}>{p => <ProviderButton p={p} onPick={() => selectProvider(p)} />}</For>
                </div>
              </div>
            </Show>
          </Match>

          {/* ── Page 1 of a protocol: which provider? ── */}
          <Match when={step() === 'preset'}>
            <Show when={provider()}>{p => (
              <PresetPage
                provider={p()}
                presets={availablePresets()}
                onPick={pr => choosePreset(pr)}
                onManual={enterManually}
              />
            )}</Show>
          </Match>

          {/* ── Page 2: the credentials for the chosen provider ── */}
          <Match when={step() === 'form'}>
            <Show when={provider()}>{p => (
              <>
                {/* What page 1 decided, and the way back to change it. */}
                <Show when={availablePresets().length > 0}>
                  <div class="connect-chosen">
                    <Show when={preset()} fallback={
                      <span class="connect-chosen-name">{t('connect.presetManual')}</span>
                    }>{pr => (
                      <>
                        <PresetLogo preset={pr()} />
                        <span class="connect-chosen-name">{pr().label}</span>
                      </>
                    )}</Show>
                    <button class="connect-chosen-change" onClick={backToPresets}>
                      {t('common.change')}
                    </button>
                  </div>
                  <Show when={preset()}>{pr => (
                    <PresetDetail preset={pr()} />
                  )}</Show>
                </Show>

                <Steps provider={p()} />
                <div class="connect-panel">
                  <For each={fieldRows()}>{(row, i) => (
                    <>
                      <div class="connect-field-row" classList={{ single: row.length === 1 }}>
                        <For each={row}>{f => (
                          <Field field={f} value={values[f.name] ?? ''} onInput={v => onFieldInput(f, v)} />
                        )}</For>
                      </div>
                      {/* The reachability check sits directly under the server +
                          port it describes, not floating above the form. */}
                      <Show when={i() === serverRowIndex() && healthText()}>
                        <HealthLine health={health()} text={healthText()} />
                      </Show>
                    </>
                  )}</For>
                  <Show when={pendingPlaceholders().length > 0}>
                    <div class="connect-placeholder-hint">
                      {t('connect.replacePlaceholder', { tokens: pendingPlaceholders().join(', ') })}
                    </div>
                  </Show>
                  <Show when={errorText()}><div class="add-error">{errorText()}</div></Show>
                </div>
              </>
            )}</Show>
          </Match>

          {/* ── Working (loading) ── */}
          <Match when={step() === 'connecting'}>
            <div class="connect-center">
              <Loader class="connect-spin" size={30} strokeWidth={1.5} />
              <div class="connect-center-title">Verifying connection…</div>
              <div class="connect-center-desc">Hang tight, we’re checking your details with {provider()?.label ?? 'the service'}.</div>
            </div>
          </Match>

          {/* ── Microsoft device code ── */}
          <Match when={step() === 'device'}>
            <Show when={provider()}>{p => <Steps provider={p()} />}</Show>
            <Show when={deviceAuth()}>{auth => (
              <div class="connect-panel connect-device">
                <div class="connect-device-label">
                  Visit{' '}
                  <span class="device-url" onClick={() => openExternal(auth().verification_url)}>{auth().verification_url}</span>
                  {' '}and enter this code:
                </div>
                <div class="connect-code-row">
                  <div class="connect-code">{auth().user_code}</div>
                  <button class="connect-copy" title={copied() ? 'Copied!' : 'Copy code'} onClick={() => copyCode(auth().user_code)}>
                    <Show when={copied()} fallback={<Copy size={15} strokeWidth={1.5} />}><Check size={15} strokeWidth={1.5} /></Show>
                  </button>
                </div>
                <div class="connect-waiting"><Loader class="connect-spin" size={14} strokeWidth={1.5} /> Waiting for sign-in…</div>
              </div>
            )}</Show>
          </Match>

          {/* ── Claude OAuth: paste the code the callback page shows ── */}
          <Match when={step() === 'paste'}>
            <Show when={provider()}>{p => <Steps provider={p()} />}</Show>
            <div class="connect-panel">
              <div class="field-col am-field">
                <label class="am-field-label">{t('connect.authCodeLabel')}</label>
                <input
                  class="text-input"
                  type="text"
                  placeholder="code#state"
                  value={pasteCode()}
                  onInput={e => setPasteCode(e.currentTarget.value)}
                />
                <div class="am-field-hint">
                  Copy the whole value shown on the page after you approve access.{' '}
                  <span class="device-url" onClick={() => beginClaude()}>{t('connect.reopenSignIn')}</span>
                </div>
              </div>
              <Show when={errorText()}><div class="add-error">{errorText()}</div></Show>
            </div>
          </Match>

          {/* ── Success ── */}
          <Match when={step() === 'success'}>
            <div class="connect-center">
              <span class="connect-badge ok"><ShieldCheck size={30} strokeWidth={1.5} /></span>
              <div class="connect-center-title">You’re connected</div>
              <div class="connect-center-desc">
                <Show when={resultEmail()} fallback={`${provider()?.label ?? 'Account'} is ready to use in your widgets.`}>
                  {resultEmail()} is ready to use in your widgets.
                </Show>
              </div>
              <button class="btn-primary" onClick={closeWindow}>{t('common.done')}</button>
            </div>
          </Match>

          {/* ── Error + troubleshooting ── */}
          <Match when={step() === 'error'}>
            <div class="connect-error-head">
              <span class="connect-badge bad"><CircleAlert size={28} strokeWidth={1.5} /></span>
              <div class="connect-center-title">{tips()?.title ?? t('connect.couldntConnect')}</div>
            </div>

            <Show when={tips()}>
              <div class="connect-panel">
                <div class="connect-tips-label">{t('connect.tryThis')}</div>
                <ul class="connect-tips">
                  <For each={tips()!.tips}>{tip => <li>{tip}</li>}</For>
                </ul>
              </div>
            </Show>

            <Show when={errorText()}>
              <details class="connect-raw">
                <summary>{t('connect.showErrorDetails')}</summary>
                <pre>{errorText()}</pre>
              </details>
            </Show>
          </Match>
        </Switch>
      </div>

      {/* Action bar: pinned to the window's bottom edge, never scrolls with the
          body. Buttons are step-specific; the terminal `success` state keeps its
          own centred Done, and `pick`/`connecting` have no actions. */}
      <Show when={step() === 'form' || step() === 'device' || step() === 'paste' || step() === 'error'}>
        <footer class="connect-footer">
          <Switch>
            <Match when={step() === 'form'}>
              <button class="btn-secondary" onClick={closeWindow}>{t('common.cancel')}</button>
              <button class="btn-primary" onClick={submitForm} disabled={cannotSubmit()}>{t('connect.connect')}</button>
            </Match>
            <Match when={step() === 'paste'}>
              <button class="btn-secondary" onClick={closeWindow}>{t('common.cancel')}</button>
              <button class="btn-primary" onClick={submitClaude} disabled={!pasteCode().trim()}>{t('connect.connect')}</button>
            </Match>
            <Match when={step() === 'device'}>
              <button class="btn-secondary" onClick={closeWindow}>{t('common.cancel')}</button>
            </Match>
            <Match when={step() === 'error'}>
              <button class="btn-secondary" onClick={closeWindow}>{t('common.close')}</button>
              <button class="btn-primary" onClick={retry}>{t('connect.tryAgain')}</button>
            </Match>
          </Switch>
        </footer>
      </Show>
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────────

/** How many brand marks fit on a third-width card before the "+N" tail. */
const BRAND_STRIP_MAX = 6;

function ProviderButton(props: { p: ConnectProvider; onPick: () => void }): JSX.Element {
  // Only the protocol providers have presets; everything else renders none.
  const cardPresets = () => isPresetProtocol(props.p.key) ? presetsFor(props.p.key) : [];
  return (
    <button class="connect-provider-btn" onClick={props.onPick}>
      <span class="connect-provider-head">
        <ProviderLogo provider={props.p.key} />
        <span class="connect-provider-name">{props.p.label}</span>
        <Show when={props.p.kind === 'oauth-device' || props.p.kind === 'oauth-browser' || props.p.kind === 'oauth-paste'}>
          <ExternalLink class="connect-provider-ext" size={13} strokeWidth={1.5} />
        </Show>
      </span>
      {/* The widgets this connection feeds, and nothing else — the marketing
          blurb ("Outlook, Office 365, Exchange Online") answered a question
          nobody was asking, while this answers the real one. [S-49]

          Spans + a CSS dash marker rather than <ul>/<li>: a button's content
          model is phrasing content, so a real list inside it is invalid HTML
          and makes screen readers announce list semantics from inside a
          control. Reads as a dashed list, stays a plain button. */}
      <span class="connect-provider-uses">
        <For each={props.p.uses}>{w => (
          <span class="connect-use">{t(`widget.${w}.label`)}</span>
        )}</For>
      </span>

      {/* Which real services this protocol reaches. "IMAP" means nothing to
          most people; the Gmail / iCloud / Fastmail marks underneath say what
          it is FOR at a glance. Applies to every protocol with presets. [S-49] */}
      <Show when={cardPresets().length > 0}>
        <span class="connect-provider-brands">
          <For each={cardPresets().slice(0, BRAND_STRIP_MAX)}>{p => (
            <PresetLogo preset={p} />
          )}</For>
          <Show when={cardPresets().length > BRAND_STRIP_MAX}>
            <span class="connect-brand-more">
              +{cardPresets().length - BRAND_STRIP_MAX}
            </span>
          </Show>
        </span>
      </Show>
    </button>
  );
}

function Steps(props: { provider: ConnectProvider }): JSX.Element {
  return (
    <Show when={props.provider.steps?.length}>
      <ol class="connect-steps">
        <For each={props.provider.steps}>{(s, i) => (
          <li><span class="connect-step-num">{i() + 1}</span><span>{s}</span></li>
        )}</For>
      </ol>
    </Show>
  );
}

/**
 * A preset's brand mark: the icon that provider publishes for itself, from the
 * generated catalog. A preset with no entry there falls back to a
 * brand-coloured monogram — a letter beats a logo drawn from memory — and both
 * sit on the same plate, so the grid reads as one set. [S-47]
 */
function PresetLogo(props: { preset: ConnectPreset }): JSX.Element {
  return (
    <Show
      when={props.preset.icon}
      fallback={
        <span
          class="preset-logo preset-logo-mono"
          style={{
            // Tint + brand-coloured glyph: the app's own subtle-fill idiom,
            // readable in both themes without per-brand contrast tuning.
            background: `color-mix(in srgb, ${props.preset.brand} 20%, transparent)`,
            color: props.preset.brand,
          }}
        >
          {props.preset.label[0]?.toUpperCase()}
        </span>
      }
    >{key => <BrandMark brand={key()} class="preset-logo" />}</Show>
  );
}

/**
 * Page one of a protocol connection: who is your provider? Picking one fills
 * the server settings, so page two only ever asks for a name and a password.
 * "Enter it myself" is always offered — presets are a shortcut, never a cage.
 * [S-47]
 */
function PresetPage(props: {
  provider: ConnectProvider;
  presets: ConnectPreset[];
  onPick: (p: ConnectPreset) => void;
  onManual: () => void;
}): JSX.Element {
  return (
    <>
      <p class="connect-lede">{t('connect.presetHint')}</p>
      <div class="connect-preset-grid">
        <For each={props.presets}>{p => (
          <button class="connect-preset-tile" onClick={() => props.onPick(p)}>
            <PresetLogo preset={p} />
            <span class="connect-preset-tile-name">{p.label}</span>
          </button>
        )}</For>

        {/* Dashed, like the Connections page's ghost row: an outline reads as
            "none of the above" rather than as another provider. */}
        <button class="connect-preset-tile manual" onClick={props.onManual}>
          <span class="preset-logo preset-logo-manual"><Pencil size={14} strokeWidth={1.5} /></span>
          <span class="connect-preset-tile-name">{t('connect.presetManual')}</span>
        </button>
      </div>
    </>
  );
}

/** What the chosen preset implies: the app-password requirement (the single
 *  biggest cause of "login failed" for these providers) and any setup step.
 *  The reachability verdict is NOT here — it belongs against the server row it
 *  judges, so it renders inside the form as `HealthLine`. [S-47] */
function PresetDetail(props: { preset: ConnectPreset }): JSX.Element {
  return (
    <div class="connect-preset-detail">
      <Show when={props.preset.appPassword}>
        <div class="connect-preset-note app-password">
          <KeyRound size={14} strokeWidth={1.5} />
          <span>
            {props.preset.note}
            <Show when={props.preset.helpUrl}>{url => (
              <>
                {' '}
                <span class="device-url" onClick={() => openExternal(url())}>{t('connect.presetOpenHelp')}</span>
              </>
            )}</Show>
          </span>
        </div>
      </Show>
      <Show when={!props.preset.appPassword && props.preset.note}>
        <div class="connect-preset-note">
          <CircleAlert size={14} strokeWidth={1.5} />
          <span>{props.preset.note}</span>
        </div>
      </Show>
    </div>
  );
}

/** The live reachability verdict for the preset's server settings, rendered
 *  directly beneath the server + port it describes. [S-47] */
function HealthLine(props: { health: PresetHealth; text: string }): JSX.Element {
  return (
    <div class="connect-preset-health" classList={{
      checking: props.health.status === 'checking',
      ok: props.health.status === 'ok',
      stale: props.health.status === 'stale',
    }}>
      <Switch>
        <Match when={props.health.status === 'checking'}>
          <Loader class="connect-spin" size={13} strokeWidth={1.5} />
        </Match>
        <Match when={props.health.status === 'ok'}>
          <Check size={13} strokeWidth={2} />
        </Match>
        <Match when={props.health.status === 'stale'}>
          <TriangleAlert size={13} strokeWidth={1.5} />
        </Match>
      </Switch>
      <span>{props.text}</span>
    </div>
  );
}
