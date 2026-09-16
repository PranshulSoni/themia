# GDPR / FADP compliance record

The internal counterpart to the public notice at
[themia.app/legal/privacy](https://themia.app/legal/privacy). The public page
tells users what happens; this file is the evidence that it is true and the
runbook for the obligations that never show up on a web page.

Keep the two in sync: **a change to processing edits both, in the same commit.**

- Controller: Nathaniel Walser, Switzerland — `themia@nathaniel-walser.com`
- Establishment: Switzerland (a third country under GDPR, with an adequacy
  decision from the European Commission)
- Applicable law: GDPR (via Art. 3(2), offering goods to data subjects in the
  Union) and the Swiss Federal Act on Data Protection (FADP)
- Data protection officer: none. Art. 37 GDPR / Art. 10 FADP do not require one:
  no large-scale monitoring, no special-category data, no public authority.

## What is NOT processed

Worth stating first, because it is most of the product. Themia is local-first:
widget settings, layouts, notes, habit/mood entries, clipboard history, files
shown by the Folder widget, and all connected-account content (mail, calendar,
contacts, drive) live only on the user's machine. Connected widgets talk
directly from the device to the provider the user chose. **None of it reaches
any system we control**, so for that data we are neither controller nor
processor, and there is nothing for us to disclose, export or erase.

Credentials and OAuth tokens are stored locally and encrypted at rest with
Windows DPAPI (per-user, machine-bound).

## Record of processing activities (Art. 30)

| # | Activity | Categories of data | Data subjects | Purpose | Legal basis | Recipient | Retention |
|---|----------|--------------------|---------------|---------|-------------|-----------|-----------|
| 1 | App usage analytics | Hashed random install id, app/OS version, architecture, device brand+model, screen, language, region, time zone, colour scheme, session counts and lengths, error source, in-app navigation | Users who consented | Product measurement (installs, active users, retention, crash rate) | Consent, Art. 6(1)(a) + ePrivacy Art. 5(3) | TelemetryDeck GmbH, Germany (processor) | While the product is measured; not linkable to a person by us |
| 2 | Website analytics | Aggregate page views, referrer, country, device/browser class, performance timings. Cookieless | Site visitors | Understanding which pages are useful | Legitimate interest, Art. 6(1)(f) | Vercel Inc., USA (processor) | ≤ 12 months |
| 3 | Licence purchase | Email, payment and billing details, country, licence key | Customers | Selling and validating Pro licences | Contract, Art. 6(1)(b); tax records Art. 6(1)(c) | Lemon Squeezy LLC, USA (merchant of record — independent controller for payment/tax) | Licence lifetime; tax retention (~10 y) at the MoR |
| 4 | Update checks | IP address in GitHub's server logs | Users | Delivering security and bug fixes | Legitimate interest, Art. 6(1)(f) | GitHub, Inc., USA (independent controller for its logs) | Per GitHub's own policy |
| 5 | Newsletter | Email address, delivery status | Subscribers | Occasional product mail | Consent, Art. 6(1)(a) | Lemon Squeezy LLC, USA (processor) | Until unsubscribe |
| 6 | Support mail | Message content, sender address, metadata | Anyone who writes in | Answering support and privacy requests | Contract Art. 6(1)(b) / legitimate interest Art. 6(1)(f) | Microsoft Corporation, USA/Ireland (processor) | ≤ 24 months after the thread ends |

No special categories (Art. 9), no criminal-offence data (Art. 10), no automated
decision-making or profiling (Art. 22).

### Legitimate-interest balancing (Art. 6(1)(f)), rows 2 and 4

Both are the minimum a hosted download can do: measurement that sets no cookie
and identifies nobody, and an update check that reveals only what any HTTPS
request reveals. Neither builds a profile, neither is combined with other data,
and neither is used to contact anyone. The impact on the data subject is close
to zero; the interest — knowing the product works and getting fixes to installed
copies — cannot be served without them. Objection: `themia@nathaniel-walser.com`.

## Consent (Art. 7)

Analytics consent is the only consent the app itself collects.

- Asked once at first run, before any identifier exists. Declining, or ignoring
  the prompt, leaves the install with **no id minted and nothing sent**
  (docs/ANALYTICS.md [S-26]).
- Freely given: the prompt is not modal, refusing costs nothing, and every app
  feature works identically either way.
- As easy to withdraw as to give: one switch, Settings → General → Data.
  The prompt and the switch call the same command.
- Pre-consent installs (v0.16.x and earlier, when analytics defaulted on) are
  **re-asked**: the old `optOut: false` was a default, not an answer, so it
  migrates to "undecided" rather than to consent ([S-25]).
- Evidence of consent: the `consent` value in `analytics.json` on the user's own
  machine. We deliberately hold no server-side consent log — that would mean
  keeping an identifier for people who said no.

## Transfers (Chapter V)

Switzerland holds an adequacy decision, so EU → CH needs no further instrument.
Onward transfers to the US processors above rest on the European Commission's
Standard Contractual Clauses and, where the provider participates, the EU–US
Data Privacy Framework. Analytics — the only telemetry the app sends — stays in
the EU (TelemetryDeck, Germany).

**Open action:** obtain and file the signed DPA / SCC package for each processor
(TelemetryDeck, Vercel, Lemon Squeezy, Microsoft). All four publish one; none is
signed on file yet. Until they are, this section describes the intended basis
rather than a documented one.

## EU representative (Art. 27) — exemption assessment

**Position: exempt under Art. 27(2)(a). Reassess when any statement below stops
being true.**

Art. 27 requires a representative in the Union unless processing is *occasional*,
*unlikely to result in a risk* to rights and freedoms, and involves no
large-scale special-category or criminal-offence data.

- **Occasional:** the app processes personal data only on the occasion of a
  purchase, a support mail, or a newsletter signup. The single continuous data
  flow, analytics, is consent-based and carries no identifier we can resolve to
  a person — and the users' actual content never leaves their machines at all.
- **Unlikely to risk rights and freedoms:** no profiling, no tracking across
  services, no advertising, no cookies, no location, no behavioural data. The
  worst case in a breach of anything we hold is an email address.
- **No special-category or criminal data**, at any scale.

Mood and habit widgets record health-adjacent notes, which *would* be Art. 9
data — but they are written to the user's own disk and never transmitted, so
they are not processed by us in any sense the GDPR recognises.

This is a judgement call, documented so it can be defended and revisited. It
should be confirmed by a lawyer before it is relied on in a procurement answer,
and revisited if EU revenue becomes a material share of sales or if any product
change starts sending user content off-device.

## Handling a data subject request

1. Acknowledge on receipt. The deadline is **one month** from receipt (Art.
   12(3)), extendable by two months for complex requests if the requester is
   told within the first month.
2. Verify identity proportionately — for a purchase, matching the order email is
   enough. Do not demand ID documents.
3. Search: the Lemon Squeezy dashboard (purchases, newsletter) and the support
   mailbox. **That is the whole search surface.** There is no app database, no
   server-side user record, and analytics hold nothing resolvable to a person.
4. Answer in writing, listing the categories, purposes, recipients and retention
   from the Art. 30 table above, plus a copy of the data where access was asked.
5. Erasure: remove the newsletter entry and the support thread; purchase records
   are retained where tax law requires and the requester is told so.
6. Free of charge, unless manifestly unfounded or excessive.

## Handling a personal data breach

1. **Contain**, then assess: whose data, how many, what categories, what harm.
2. **Notify the supervisory authority within 72 hours** of becoming aware,
   unless the breach is unlikely to result in a risk (Art. 33). As a Swiss
   controller with EU data subjects, notify the FDPIC and — where the exemption
   above no longer holds or an EU authority is competent — the relevant EU
   authority.
3. **Notify affected users without undue delay** where the risk is high (Art.
   34). The plausible cases here involve a processor: a compromise at the
   merchant of record or the mail provider.
4. **Record every breach**, including ones not notified and why (Art. 33(5)).
   Keep the record in this repository, below.

Breach log: *(none to date)*

## Technical and organisational measures (Art. 32)

- TLS on everything the app sends; certificate validation is never disabled
  (enforced by a source-scanning test).
- Secrets at rest on the user's machine encrypted with Windows DPAPI, per-user
  and machine-bound.
- Widgets are blocked from private, loopback and local-network addresses unless
  the user opts in (SSRF guard).
- Analytics are built entirely in the Rust backend; the webview can only send
  what it is handed, so a frontend fault cannot send for a user who refused
  (docs/ANALYTICS.md).
- Error telemetry carries the failing subsystem's name only — never message
  text, paths or addresses ([S-22], [S-24]).
- Data minimisation is tested, not remembered: the never-sent list is pinned by
  unit tests, and a "no Google tags" source scan fails the website build if an
  advertising or analytics tag is reintroduced.

## Open items

| Item | Why it matters | Status |
|------|----------------|--------|
| Signed DPAs / SCCs from all four processors | Art. 28(3) requires a written contract with each processor | Not started |
| Legal review of the Art. 27 exemption | It is our own assessment, not an opinion | Not started |
| Postal address in the public notice | Art. 13(1)(a) expects contact details; we publish name, country and email only | Deliberate, residual gap |
| Cookie/consent banner | Not needed while website analytics stay cookieless — keep it that way | Not required |
