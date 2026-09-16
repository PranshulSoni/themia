# Analytics (anonymous usage telemetry)

Themia sends **anonymous** usage analytics to [TelemetryDeck](https://telemetrydeck.com)
using their official [`@telemetrydeck/sdk`](https://github.com/TelemetryDeck/JavaScriptSDK).

Before v0.15.0 this was Google Analytics 4 via the Measurement Protocol. GA4 was
replaced because it is a web-shaped product being bent around a desktop app; the
migration notes are at the bottom of this file.

## Architecture: consent in Rust, transport in the webview

TelemetryDeck ships **no Rust SDK** (Swift, Kotlin, Flutter, Web, JavaScript,
React/React Native, Vue and Obj-C only), so their official client can only run in
the webview. Responsibility is split so that using it costs nothing in consent
guarantees:

| Half | Owns | Lives in |
| ---- | ---- | -------- |
| Backend | consent, cadence, identity, delivery markers — the only thing that touches `analytics.json` | `src-tauri/src/analytics.rs` |
| Frontend | transport: configures the SDK and puts signals on the wire | `src/analytics.ts` |

The protocol is:

1. `analytics_plan(env)` → `{ appId, clientUser, target, testMode, signals[] }`.
   Each signal is **fully built by the backend**: `{ type, payload, marker }`.
   The frontend **can only send what appears in `signals`**, so a fault in the
   webview can neither send for a user who opted out nor attach anything of its
   own. An empty list means: opted out, unconfigured, nothing due today, or
   another window already claimed it. `env` is the frontend's one contribution —
   the handful of facts only a webview can see ([S-18]).
2. `td.signal(type, payload)` for each — the SDK's own HTTP call.
3. `analytics_mark_sent(marker)` on a success response, which is the **only**
   place a delivery marker is persisted. A failed send is retried next launch.
   A signal with `marker: null` is sent but never confirmed: session starts,
   errors and navigations are facts about *now*, so re-sending them later would
   be wrong rather than helpful.

`analytics_track(event)` returns a plan holding one signal for something that
just happened (an error, a navigation, a license activation), or an empty plan
when the user opted out — the consent gate is the same one ([S-21]).

`analytics_set_optout(optOut)` returns a plan holding the single `app_opt_out`
signal on the on→off transition, and an empty plan otherwise ([S-07]).

`analytics_session_end(seconds)` records how long a session ran. It persists
only; nothing is sent.

Themia opens one webview per monitor and all of them run this on startup, so the
backend hands each event to whichever window asks first ([S-14]). A three-monitor
setup sends one heartbeat, not three.

The frontend re-asks hourly so a machine left running overnight still reports the
next day. The interval is only a poll — the once-a-day decision is the backend's
on-disk day marker, so it is telemetry cadence, never a correctness path.

### Network / proxy

The SDK uses `fetch` inside WebView2, which follows the **Windows system proxy**.
This is a change from the GA4-era sender, which used `widget_netproxy`'s reqwest
builder and therefore also honoured the explicit/automatic-configuration boxes
(netproxy SPEC [PX-01]). Standard system-proxy setups are unaffected; an exotic
proxy configuration that only `widget_netproxy` resolved would now go unreported.
Telemetry is best-effort, so this was accepted rather than keeping a second,
hand-rolled sender alive next to the official SDK.

## Goals

| Question                       | How it's answered                                              |
| ------------------------------ | ------------------------------------------------------------- |
| How many installs / devices?   | the Acquisition dashboard, off `TelemetryDeck.Acquisition.newInstallDetected` |
| Monthly active users (MAU)?    | distinct `clientUser`s with any signal in the month; the heartbeat guarantees one per active day |
| How engaged are they?          | the Activation/Retention dashboards, off the session signal and the `TelemetryDeck.Retention.*` parameters |
| What breaks, and where?        | the Errors dashboard, grouped by `TelemetryDeck.Error.id` (= the log source) |
| Which screens get used?        | `TelemetryDeck.Navigation.pathChanged`                          |
| Churn / uninstalls?            | **inferred** from heartbeat absence: Windows gives apps no uninstall callback, so there is no exact uninstall signal |

TelemetryDeck's `clientUser` is a real, stable, per-install identifier, so active
users and retention are first-class metrics rather than something reconstructed
from session counts.

## What is collected

Identity, on every signal:

- `clientUser`: the **SHA-256 hash** of a random UUID generated on first run and
  stored in `analytics.json`. The raw UUID never leaves the machine. The UUID is
  not derived from any hardware or user identifier, so the hash identifies an
  install and nothing else. TelemetryDeck salts and hashes it again server-side.
- `sessionID`: random, generated once per app launch. Groups the signals of one
  run; not persisted, so it cannot link two launches.

TelemetryDeck's **default parameters**, using its own names so the dashboards
group them without custom configuration ([S-16]). The SDK contributes none of
these — it knows nothing about the app it is embedded in — so the backend builds
the lot:

| Group | Parameters | Source |
| ----- | ---------- | ------ |
| AppInfo | `version` | `CARGO_PKG_VERSION` |
| Device | `platform`, `operatingSystem` (`Windows`), `architecture`, `systemVersion`, `systemMajorVersion`, `systemMajorMinorVersion`, `modelName`, `brand` | Rust: `RtlGetVersion` + the BIOS registry keys |
| Device | `timeZone`, `screenResolutionWidth/Height`, `screenScaleFactor` | webview ([S-18]) |
| RunContext | `isDebug`, `isSimulator`, `isTestFlight`, `isAppStore`, `targetEnvironment`, `language`, `locale` | build flags + webview |
| UserPreference | `language`, `region`, `colorScheme`, `layoutDirection` | webview |
| Acquisition | `firstSessionDate` | `analytics.json` |
| Retention | `totalSessionsCount`, `distinctDaysUsed`, `distinctDaysUsedLastMonth`, `averageSessionSeconds`, `previousSessionSeconds` | `analytics.json` ([S-19]) |

A value the OS or the webview won't give us is **left out**, never faked: an
absent key reads as "unset" in the dashboard, an invented one reads as a fact.

Two spellings matter and are pinned by tests. `platform` is `Windows`, not
Rust's lowercase `windows` — the dashboard groups verbatim, so the two would be
different platforms. And Windows 11 still reports kernel version 10.0, so the
major number is corrected to 11 from build 22000 upwards ([S-17]); reporting it
verbatim would file every Windows 11 machine under "10".

**Never** collected: email, account names, license key, file paths, window
contents, machine name, view or monitor names, log message text, or the raw
install UUID.

This is what the Settings copy promises ("a random install ID, app and system
version, and how often the app is used"). Adding a field here means updating
`settings.analytics.desc` in `src/locales/*.ts` too.

## Events

Event names are carried in the signal's `type` field. They are **TelemetryDeck's
own names**, so its built-in dashboards work with no custom insight:

- `TelemetryDeck.Acquisition.newInstallDetected`: **once ever** per install.
  This is the signal the Acquisition dashboard counts as a new user; our
  previous custom `app_first_run` name was invisible to it ([S-20]).
- `TelemetryDeck.Session.started`: **once per app launch**, deduped across the
  per-monitor windows. Drives the Activation dashboard's session metrics.
- `TelemetryDeck.Error.occurred`: carries `TelemetryDeck.Error.id` and
  `.category`. Wired to the ERROR level of the log pipeline, so it covers every
  subsystem plus the uncaught-exception and unhandled-rejection handlers with no
  extra call sites. Only the log **source** travels — never the message, which
  routinely holds paths, addresses and server responses ([S-24]). One signal per
  distinct source per window, capped at 25.
- `TelemetryDeck.Navigation.pathChanged`: on opening an auxiliary window and on
  moving between config pages. Paths are ours and fixed (`settings/license`,
  `widget/notes`, `tray-flyout`); every per-install id is stripped ([S-23]).
- `TelemetryDeck.Purchase.completed`: on a successful license activation, with
  the tier as `productID`. The key itself never goes near analytics.
- `app_heartbeat` (custom): **at most once per calendar day**, deduped via
  `lastHeartbeatYmd`. Covers the machine that is never restarted, which would
  otherwise stop counting as active after its single session signal.
- `app_opt_out` (custom): sent **once**, the moment a user turns analytics off.
  Delivered before the subsystem goes quiet; nothing else is sent afterwards.

TelemetryDeck has no reserved event-name list, so unlike GA4 there is no hazard
of a name being silently discarded.

## Privacy / consent

**Opt-in, off until asked.** Storing an identifier on someone's machine for
analytics needs consent first (ePrivacy Art. 5(3)), so consent is a
**three-state** decision, not a checkbox:

| `consent` | Sends | May be asked | Meaning |
| --- | --- | --- | --- |
| `unset` | nothing | yes | the first-run prompt has not been answered |
| `granted` | everything due | no | the user said yes |
| `denied` | nothing | no | the user said no |

- The on-disk `consent` value in `analytics.json` is the single source of truth
  and is read by the **backend** on every plan, so a frontend fault can never
  send for someone who did not agree.
- `unset` is exactly as silent as `denied`. It differs in one respect only: we
  may still ask. An unanswered prompt is never read as a "no" — ignore the card
  and it comes back next launch.
- **Nothing is written before the answer** ([S-26]): no install UUID is minted,
  and session/retention bookkeeping does not persist. Declining leaves no
  identifier behind at all, because none was ever created.
- One prompt per install, and per launch one window shows it ([S-27]) — the
  same claiming that keeps a three-monitor setup to one heartbeat.
- The prompt and **Settings → General → Data → "Share anonymous usage data"**
  are the same decision and call the same command, `analytics_set_consent`.
  There is no second path.

### Migration from the opt-out era ([S-25])

v0.16.x and earlier were opt-out, with a boolean `optOut`. On first read the old
key is folded into the new one and then dropped from the file:

- `optOut: true` → `denied`. That was an explicit refusal and is honoured
  forever; those users are never re-prompted.
- `optOut: false` → `unset`. That was the *default*, not a decision, so it
  cannot be treated as consent. Every such install is asked once.
- A file that already carries `consent` keeps it; the migration never overwrites
  an answer.

**Expect the active-user series to step down** the week this ships: only
installs that answer yes keep reporting.

## Dev vs production data

Every build POSTs to the same ingest endpoint. Debug/dev builds set
`isTestMode: true` on every signal, which TelemetryDeck excludes from production
queries natively (no dashboard configuration needed, unlike GA4's Internal
Traffic filter). Release builds omit the field.

## Credentials + endpoint (committed in source)

Two constants in `src-tauri/src/analytics.rs`:

```
TD_APP_ID  = "67AD8F18-8230-41A1-ADF2-13296ADB8AB6"
INGEST_URL = "https://nom.telemetrydeck.com/v2/namespace/com.nathaniel-walser/"
```

The app ID is committed on purpose. It is a write-only client key that ships
inside the distributed binary and is therefore trivially extractable, so keeping
it out of source buys nothing. Worst case is signal spam to this app, which is
mitigated (and the app ID is rotatable) in the TelemetryDeck dashboard. There is
no separate API secret: TelemetryDeck's ingest endpoint authenticates on the app
ID alone.

The URL names the `com.nathaniel-walser` namespace explicitly. The bare
`https://nom.telemetrydeck.com/v2/` that TelemetryDeck's own SDKs default to is
also accepted (both were probed and return `200 OK`), with the server resolving
the namespace from the app ID — but spelling the target out means a future
change to that server-side default cannot silently reroute Themia's data.

When `TD_APP_ID` is empty the whole subsystem is a no-op: nothing is sent and a
warning is logged once. That is what an unconfigured build does, not a silent
send to nowhere. Test `s04_app_id_is_compiled_in` guards against shipping a
release in that state by accident.

### Live verification

`diag_analytics_test` (a `diag_*` command) reports what the backend would hand
out right now — target, app ID, test mode, the current opt-out flag, what is due
and what has already been claimed this run — **without** claiming or sending
anything. It answers "is analytics configured and would it fire?" from any
build, with no side effects on the data.

To confirm signals actually land, watch the app log (`analytics` target): the
frontend logs each delivered event, and the backend logs each plan it hands out.
Debug builds send with `isTestMode: true`, so enable **Test mode** on the app in
the TelemetryDeck dashboard to see them.

## Behaviors (spec tags, mirror in test names)

- **[S-01]** Absent or corrupt `analytics.json`, or an unknown `schemaVersion`,
  yields a typed default, never a half-built config.
- **[S-02]** A valid v1 config round-trips every field.
- **[S-03]** Consent is tri-state and defaults to `unset`. Both `unset` and
  `denied` send nothing; only `granted` sends. The Settings toggle renders
  `granted` as on, and calls the same command as the first-run prompt.
- **[S-04]** Signals are sent only when an app ID is configured **and** consent
  is `granted`. Anyone else gets an empty plan, so the frontend is physically
  unable to send.
- **[S-05]** The plan carries `appId`, the hashed `clientUser`, `target`,
  `testMode` and a closed set of event names, and never the raw install UUID.
  Event names cross IPC as a discriminated union: an unknown value is rejected,
  not forwarded to TelemetryDeck.
- **[S-06]** Debug builds set `testMode: true`; release builds send `false`.
- **[S-07]** A single `app_opt_out` event fires only when a consent that was
  actually given is withdrawn (`granted` → `denied`), never repeated, and not
  gated by the answer it announces. Declining the first-run prompt
  (`unset` → `denied`) sends nothing: there was no consent to leave under.
- **[S-08]** A heartbeat is due at most once per calendar day; repeat launches
  the same day send nothing.
- **[S-09]** `clientUser` is the SHA-256 of the install UUID: never the raw
  UUID, stable for a given install, and different for different installs.
- **[S-10]** A GA-era config with the legacy `installReported: true` key does
  not suppress the first-run signal. (Historic: that key marked GA4's
  `app_install` delivered, but GA silently dropped every one of those as a
  reserved name, so the once-ever signal was still owed.)
- **[S-11]** A GA-era config with `firstRunReported: true` does not suppress the
  TelemetryDeck first-run signal: those events went to GA4, so TelemetryDeck is
  still owed one. Delivery is keyed to `firstRunSentTd`; `installId` and the
  opt-out choice are preserved untouched, so nobody is silently re-enabled.
- **[S-12]** One `sessionID` per app launch, generated by the SDK: stable across
  every signal of a run, and not persisted across runs.
- **[S-14]** Per-event claiming dedupes across windows: with one webview per
  monitor, the first to ask gets the work and the rest get an empty plan. Claims
  are in-memory, so an unconfirmed event is retried next launch, not lost.
- **[S-15]** Frontend protocol: an empty plan builds no SDK client and touches
  no network; each delivered signal with a marker is confirmed back exactly
  once, and an unmarked one never is; a rejected or throwing send is never
  confirmed and does not abort the rest of the plan; a failure to reach the
  backend is not fatal to startup.
- **[S-16]** Every signal carries TelemetryDeck's full default parameter set,
  built by the backend. A value that can't be determined is omitted, never
  faked.
- **[S-17]** The OS version is reported as `systemVersion`,
  `systemMajorMinorVersion` and `systemMajorVersion` (the dashboard offers all
  three as Top-N dimensions and derives none of them). Windows 11 is corrected
  from kernel 10.0 to major 11 at build 22000. `platform` and `operatingSystem`
  are spelled `Windows`, as TelemetryDeck's own SDKs spell them.
- **[S-18]** The webview reports only environment (locale, region, colour
  scheme, layout direction, time zone, screen). Every string is trimmed,
  stripped of control characters and length-capped; the struct denies unknown
  fields, so nothing else can be smuggled onto a signal.
- **[S-19]** Acquisition/retention state lives in `analytics.json`: one session
  per launch, one distinct day per calendar day, first-session date pinned once,
  history capped at 400 days, and a session length clamped to 24h so a suspended
  machine can't poison the average.
- **[S-20]** Lifecycle events use TelemetryDeck's own signal names, so the
  Acquisition and Activation dashboards count them without a custom insight.
- **[S-21]** Tracked events (error, navigation, purchase) are a closed set with
  typed fields. An unknown event, an unknown category or an extra parameter is
  rejected at the IPC boundary, and an opted-out user gets an empty plan.
- **[S-22]** An error message may only be a short static description. Anything
  shaped like user data — an address, a path, a URL, a drive letter — is dropped
  and logged, because that is precisely what TelemetryDeck warns against.
- **[S-23]** Navigation paths are fixed names of ours. View ids, monitor ids and
  widget instance ids never appear; a widget page reports its TYPE only.
- **[S-24]** Errors are reported by hooking the log pipeline's ERROR level.
  Only the source travels, `analytics` itself is excluded (it would feed
  itself), and each source is reported at most once per window, capped at 25.

- **[S-25]** The pre-consent `optOut` flag migrates once: `true` → `denied`
  (an explicit refusal, honoured forever), `false` → `unset` (the old default,
  never an answer, so the user is asked). An existing `consent` value always
  wins, and the legacy key is not written back.
- **[S-26]** No analytics state exists before consent: the install UUID is
  minted on `granted` and not before, and session/retention bookkeeping does
  not persist while the decision is `unset` or `denied`.
- **[S-27]** The first-run prompt is claimed by one window per process and only
  while consent is `unset`. The claim is in-memory, so a window closed without
  answering asks again next launch rather than counting as a refusal.

Unit tests: `src-tauri/src/analytics.rs` (`#[cfg(test)] mod tests`) for consent,
migration, cadence and the plan; `src/analytics.test.ts` for the frontend
send/confirm protocol.
UI toggle: `tests/e2e/specs/74-analytics-consent.spec.ts`. The first-run card
itself (`src/components/AnalyticsConsent.tsx`) is verified manually: a running
app has always answered by the time a spec can look at it.

## Migration from GA4 (v0.15.0)

- `analytics.json` keeps `schemaVersion: 1`. The file is **not** rewritten from
  scratch, so `installId` and — critically — `optOut` survive the switch. A
  version bump would have discarded the file and silently re-enabled analytics
  for anyone who had turned it off.
- Delivery of the once-ever install signal is re-keyed from `firstRunReported`
  to `firstRunSentTd`, which defaults to `false`. Every existing install
  therefore sends one catch-up install signal on the next launch after
  upgrading, so TelemetryDeck gets a correct install baseline. **Expect a
  one-time install spike** the week 0.15.0 rolls out.
- Historic GA4 data stays in GA4; it is not backfilled. The two datasets do not
  share identifiers, by design.
- Retired: the GA4 reserved-event-name guard (old [S-09]) and the
  validation-endpoint split (old [S-06]). Neither concept exists in
  TelemetryDeck.

## Going native (v0.16.0)

v0.15.0 kept the GA4-era shape — a couple of custom event names and a two-key
payload — pointed at a new provider. v0.16.0 adopts TelemetryDeck's own model
instead, so its built-in dashboards do the work rather than hand-built insights:

- The custom `app_first_run` name is replaced by
  `TelemetryDeck.Acquisition.newInstallDetected`. **Any dashboard insight keyed
  on `app_first_run` must be repointed**; installs before the upgrade keep the
  old name, so a chart that needs continuity has to accept either.
- Session, error, navigation and purchase signals are new. The deliberate
  one-signal-per-day budget is gone: an active install now sends a session
  signal per launch, plus a signal per navigation and per distinct error source.
  Volume is still small (no per-interaction telemetry), and the daily heartbeat
  stays for machines that are never restarted.
- `analytics.json` keeps `schemaVersion: 1`. The retention fields are added with
  serde defaults, so an existing file keeps its `installId` and its opt-out
  choice and simply starts its history at zero. Retention numbers are therefore
  meaningless for the first month after upgrading — `firstSessionDate` is the
  first launch *after* the upgrade, not the true install date.
