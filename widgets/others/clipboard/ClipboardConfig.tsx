import { Clipboard } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';

interface ClipboardConfigData {
  maxItems?: number;
  [k: string]: unknown;
}

interface ClipboardConfigProps {
  config: ClipboardConfigData;
  onChange: (c: ClipboardConfigData) => void;
}

export default function ClipboardConfig(props: ClipboardConfigProps) {
  // The hand-rolled clamp is gone: NumberStepper enforces min/max itself.

  return (
    <ConfigTabs tabs={[{ key: 'general', label: t('common.general'), icon: Clipboard }]}>
      {() => (
        <>
          {/* Was `.field` + `.section-label` (a section HEADING used as a field
              label) + a raw number input, with the explanation as an inline-
              styled <p> that hand-rolled `.design-desc` at a different size and
              colour than the base. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('clipboard.historySize.label')}</span>
              <span class="design-desc">{t('clipboard.historySize.desc')}</span>
            </div>
            <NumberStepper
              value={props.config.maxItems ?? 50}
              min={1}
              max={200}
              onChange={(v) => props.onChange({ ...props.config, maxItems: v })}
            />
          </div>
        </>
      )}
    </ConfigTabs>
  );
}
