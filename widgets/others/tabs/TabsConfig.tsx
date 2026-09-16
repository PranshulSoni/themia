import ChildListConfig from '../../../src/components/ChildListConfig.tsx';

interface TabsConfigProps {
  config: { activeChildId?: string | null };
  onChange: (c: { activeChildId?: string | null }) => void;
  _widgetId?: string;
  globals?: { onAddToContainer?: (containerId: string) => void };
}

// Tabs is a container: its config is the shared child list (add / reorder /
// pop out). This used to be a byte-for-byte copy of StackConfig, differing only
// in a config interface whose field the body never read.
export default function TabsConfig(props: TabsConfigProps) {
  return (
    <ChildListConfig
      labelKey="tabs.childrenLabel"
      emptyKey="tabs.childrenEmpty"
      addKey="tabs.addChild"
      widgetId={props._widgetId}
      globals={props.globals}
    />
  );
}
