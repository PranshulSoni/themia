import { Show } from 'solid-js';
import { toast } from '../toastStore.ts';
import './Toast.css';

export default function Toast() {
  return (
    <Show when={toast()}>
      {(t) => (
        <div class={`app-toast ${t().kind}`} role="status" data-themia-interactive="true">
          {t().message}
        </div>
      )}
    </Show>
  );
}
