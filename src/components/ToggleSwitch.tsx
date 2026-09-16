import type { JSX } from 'solid-js';
import { t } from '../i18n.ts';
import './ToggleSwitch.css';

export interface ToggleSwitchProps {
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
  title?: string;
  /** Label shown next to the switch when checked. Default "On". */
  onLabel?: string;
  /** Label shown next to the switch when unchecked. Default "Off". */
  offLabel?: string;
  'aria-label'?: string;
  'aria-labelledby'?: string;
  class?: string;
}

export default function ToggleSwitch(props: ToggleSwitchProps): JSX.Element {
  const stateText = () => props.checked
    ? (props.onLabel ?? t('toggle.on'))
    : (props.offLabel ?? t('toggle.off'));
  return (
    <span class="toggle-switch-wrap">
      <span class="toggle-switch-state">{stateText()}</span>
      <button
        type="button"
        role="switch"
        aria-checked={props.checked}
        aria-label={props['aria-label']}
        aria-labelledby={props['aria-labelledby']}
        disabled={props.disabled}
        title={props.title}
        class={`design-switch kbd-focusable${props.checked ? ' on' : ''}${props.class ? ' ' + props.class : ''}`}
        onClick={() => { if (!props.disabled) props.onChange(!props.checked); }}
      />
    </span>
  );
}
