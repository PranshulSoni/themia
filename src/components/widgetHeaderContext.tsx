import { createContext, useContext } from 'solid-js';
import type { HeaderMode } from '../widgets/registry.tsx';
import type { FlyoutSize } from '../trayLayout.ts';

// Carries a widget's shared chrome fields (header title + mode, background
// transparency, tray flyout size) into ConfigTabs so it can render them at the
// top of the config page, without every widget config component having to
// thread the props through. Provided by the unified config window
// (ConfigWindow.tsx); consumed by ConfigTabs.
export interface WidgetHeaderInfo {
  label: () => string;             // current header override
  defaultLabel: () => string;      // registry label, used as placeholder
  noHeader: () => boolean;         // widget type renders without a header (registry)
  headerMode: () => HeaderMode;    // resolved current mode (WidgetHeader.SPEC [S-14])
  canTransparent: () => boolean;   // widget type offers the transparent background (registry, [S-32])
  transparentBg: () => boolean;    // this instance's current transparency ([S-32])
  /** This instance's flyout size when it lives in the TRAY, else null — which
   *  is what hides the section for an ordinary desktop widget (Tray.SPEC
   *  [S-07]). A per-instance app-level setting like the two above, so it
   *  belongs on the widget's own page rather than on a separate tray page. */
  trayFlyout: () => FlyoutSize | null;
  /** This instance's tray name, or '' when it inherits (Tray.SPEC [S-14]). */
  trayName: () => string;
  /** What the tooltip shows while `trayName` is empty, i.e. the placeholder. */
  trayNameFallback: () => string;
  /** Icon-catalogue key overriding the tray icon, '' for the widget's own. */
  trayIcon: () => string;
  /** [S-45] Can this instance be pinned above other applications? False for a
   *  tray widget (it has no desktop overlay) and for a container's child (it
   *  follows its top-level ancestor's layer), which is what hides the row. */
  pinnable: () => boolean;
  /** [S-45] This instance's current always-on-top state. */
  alwaysOnTop: () => boolean;
  onLabelChange: (value: string) => void;
  onHeaderModeChange: (mode: HeaderMode) => void;
  onTransparentBgChange: (value: boolean) => void;
  onAlwaysOnTopChange: (value: boolean) => void;
  onTrayFlyoutChange: (size: Partial<FlyoutSize>) => void;
  onTrayNameChange: (value: string) => void;
  onTrayIconChange: (key: string) => void;
}

export const WidgetHeaderContext = createContext<WidgetHeaderInfo>();

export function useWidgetHeader(): WidgetHeaderInfo | undefined {
  return useContext(WidgetHeaderContext);
}
