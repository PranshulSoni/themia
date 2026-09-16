import { createMemo } from 'solid-js';
import { t } from '../../../src/i18n.ts';
import MultiSelectDropdown, { type MultiSelectItem } from '../../../src/components/MultiSelectDropdown.tsx';
import { COLUMN_DEFS, resolveColumns } from './columns.ts';

interface ColumnMultiSelectProps {
  value?: string[] | Record<string, boolean>;
  onChange: (visible: string[]) => void;
  placeholder?: string;
}

export default function ColumnMultiSelect(props: ColumnMultiSelectProps) {
  const items = createMemo<MultiSelectItem[]>(() =>
    COLUMN_DEFS.map(c => ({ key: c.key, label: c.label(), icon: c.icon }))
  );
  const visible = createMemo(() => resolveColumns(props.value).visible);

  return (
    <MultiSelectDropdown
      items={items()}
      visibleKeys={visible()}
      onChange={props.onChange}
      placeholder={props.placeholder}
      searchPlaceholder={t('folder.columnsSearchPlaceholder')}
    />
  );
}
