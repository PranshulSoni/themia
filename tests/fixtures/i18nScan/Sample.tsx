// Fixture for tests/i18nScan.test.ts — NOT shipped code.
//
// Holds one of each case the scanner must judge, so a regex that silently
// stops matching is caught by the scanner's own tests rather than by a user
// finding English text in a translated build.
export default function Sample() {
  return (
    <div>
      {/* offender: a literal in a scanned slot */}
      <span class="design-label">Refresh interval</span>
      {/* clean: routed through t() */}
      <span class="design-desc">{t('sample.translated')}</span>
      {/* clean: a brand name is not translated */}
      <span class="design-label">GitHub</span>
      {/* clean: not a scanned slot */}
      <span class="sample-body">Some prose the scanner ignores</span>
    </div>
  );
}

// offender: registry/catalog data properties, invisible to the class scan
export const sampleWidget = {
  description: 'Shows something useful on your desktop',
  blurb: 'One line about this connection',
};

// clean: the same properties, routed through t()
export const translatedWidget = {
  get description() { return t('sample.description'); },
  get blurb() { return t('sample.blurb'); },
};

// clean: a dev-only harness is stripped from release builds, so its fixture
// strings are never user-facing. Guards devRanges() brace matching.
if (import.meta.env.DEV) {
  const harness = {
    label: 'My Office Layout',
    description: 'Dev harness fixture nobody ships',
  };
  void harness;
}
