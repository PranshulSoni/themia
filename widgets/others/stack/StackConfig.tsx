import ChildListConfig from '../../../src/components/ChildListConfig.tsx';

interface StackConfigData {
  expandedId?: string | null;
}

interface StackConfigProps {
  config: StackConfigData;
  onChange: (c: StackConfigData) => void;
  _widgetId?: string;
  globals?: { onAddToContainer?: (containerId: string) => void };
}

// Stack is a container: its config is the shared child list (add / reorder /
// pop out).
export default function StackConfig(props: StackConfigProps) {
  return (
    <ChildListConfig
      labelKey="stack.childrenLabel"
      emptyKey="stack.childrenEmpty"
      addKey="stack.addChild"
      widgetId={props._widgetId}
      globals={props.globals}
    />
  );
}
