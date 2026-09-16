import { Volume2 } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface VolumeConfigProps {
  config: Record<string, unknown>;
  onChange: (c: Record<string, unknown>) => void;
}

export default function VolumeConfig(_props: VolumeConfigProps) {
  return (
    <ConfigTabs tabs={[{ key: 'about', label: t('common.general'), icon: Volume2 }]}>
      {() => (
        // The inline-styled <p> hand-rolled `.design-desc` with a line-height
        // that had already drifted (1.5 vs the base's 1.4).
        <>
          <span class="section-label">{t('volume.about.title')}</span>
          <span class="design-desc">{t('volume.about.body')}</span>
        </>
      )}
    </ConfigTabs>
  );
}
