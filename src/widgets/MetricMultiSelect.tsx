import { createMemo } from 'solid-js';
import MultiSelectDropdown, { type MultiSelectItem } from '../components/MultiSelectDropdown.tsx';
import { type MetricDef, resolveMetrics } from './metricSet.ts';
import { t } from '../i18n.ts';

interface MetricMultiSelectProps {
  defs: MetricDef[];
  value?: unknown;
  onChange: (visible: string[]) => void;
  placeholder?: string;
  reorder?: boolean;
}

export default function MetricMultiSelect(props: MetricMultiSelectProps) {
  const items = createMemo<MultiSelectItem[]>(() =>
    props.defs.map(d => ({
      key: d.key,
      label: d.label(),
      icon: d.icon,
      reorderDisabled: !props.reorder,
    }))
  );
  const visible = createMemo(() => resolveMetrics(props.value, props.defs).visible);

  return (
    <MultiSelectDropdown
      items={items()}
      visibleKeys={visible()}
      onChange={props.onChange}
      placeholder={props.placeholder ?? 'Select metrics'}
      searchPlaceholder={t('widgetUi.searchMetrics')}
    />
  );
}
