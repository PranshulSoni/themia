import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import '../../../src/styles/config-base.css';

interface QuoteConfigProps {
  config: Record<string, unknown>;
  onChange: (c: Record<string, unknown>) => void;
}

// The widget has no settings of its own — the explanatory "Quote of the day"
// blurb that used to fill this panel was removed. The panel stays because
// ConfigTabs still renders the shared Header (title / style) and Background
// (transparency, [S-32]) sections; dropping `configComponent` from the registry
// would take those with it.
export default function QuoteConfig(_props: QuoteConfigProps) {
  return <ConfigTabs tabs={[]}>{() => null}</ConfigTabs>;
}
