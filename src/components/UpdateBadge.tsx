import { CircleFadingArrowUp, X } from 'lucide-solid';
import { t, tm } from '../i18n.ts';
import { dismissUpdateVersion } from '../updateBadge.ts';

interface UpdateBadgeProps {
  /** The offered version, e.g. `1.13.0`. Shown as `v1.13.0 available`. */
  version: string;
  /** Open the update surface (Settings → Updates). Never installs directly. */
  onOpen: () => void;
}

/**
 * [S-35] The quiet bottom-right "a newer version is out" pill.
 *
 * Lives in `.corner-badges` and carries `data-themia-interactive`, so the
 * desktop pass-through mask ([S-17]) keeps it clickable. Dismissal writes
 * through to `updateBadge.ts` (per-version, shared across windows) rather than
 * being local state, so every monitor's badge drops in one gesture.
 */
export default function UpdateBadge(props: UpdateBadgeProps) {
  return (
    <div class="update-badge" data-themia-interactive="true">
      <button
        class="update-badge-open"
        title={t('app.updateBadgeTitle', { version: props.version })}
        onClick={() => props.onOpen()}
      >
        <CircleFadingArrowUp size={13} strokeWidth={1.5} />
        <span>{t('app.updateBadge', { version: props.version })}</span>
      </button>
      <button
        class="update-badge-dismiss"
        title={tm('app.updateBadgeDismiss')()}
        aria-label={tm('app.updateBadgeDismiss')()}
        onClick={() => dismissUpdateVersion(props.version)}
      >
        <X size={12} strokeWidth={1.5} />
      </button>
    </div>
  );
}
