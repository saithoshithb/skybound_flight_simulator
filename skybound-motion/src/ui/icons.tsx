// Small stroke icon set for the flight UI (24×24 grid, currentColor).
import type { SVGProps } from 'react';

type P = SVGProps<SVGSVGElement> & { size?: number };

function Icon({ size = 18, children, ...props }: P & { children: React.ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      {...props}
    >
      {children}
    </svg>
  );
}

export const PauseIcon = (p: P) => (
  <Icon {...p}>
    <path d="M9 5v14M15 5v14" />
  </Icon>
);
export const PlayIcon = (p: P) => (
  <Icon {...p}>
    <path d="M8 5.5v13l10.5-6.5z" />
  </Icon>
);
/** Camera view: chase ↔ cockpit. */
export const CameraIcon = (p: P) => (
  <Icon {...p}>
    <path d="M3 8.5A1.5 1.5 0 0 1 4.5 7h3l1.5-2h6l1.5 2h3A1.5 1.5 0 0 1 21 8.5v9A1.5 1.5 0 0 1 19.5 19h-15A1.5 1.5 0 0 1 3 17.5z" />
    <circle cx="12" cy="13" r="3.5" />
  </Icon>
);
/** Instrument panel. */
export const GaugeIcon = (p: P) => (
  <Icon {...p}>
    <path d="M4.5 17a8 8 0 1 1 15 0" />
    <path d="M12 13l4-4" />
    <circle cx="12" cy="13" r="1.2" />
  </Icon>
);
export const HelpIcon = (p: P) => (
  <Icon {...p}>
    <rect x="3" y="6" width="18" height="12" rx="2" />
    <path d="M7 10h1M11 10h1M15 10h2M7 14h10" />
  </Icon>
);
export const SettingsIcon = (p: P) => (
  <Icon {...p}>
    <path d="M4 7h10M18 7h2M4 17h4M12 17h8" />
    <circle cx="16" cy="7" r="2" />
    <circle cx="10" cy="17" r="2" />
  </Icon>
);
/** Recenter the motion controller. */
export const RecenterIcon = (p: P) => (
  <Icon {...p}>
    <circle cx="12" cy="12" r="7" />
    <circle cx="12" cy="12" r="1.5" />
    <path d="M12 2v3M12 19v3M2 12h3M19 12h3" />
  </Icon>
);
export const PlusIcon = (p: P) => (
  <Icon {...p}>
    <path d="M12 5v14M5 12h14" />
  </Icon>
);
export const MinusIcon = (p: P) => (
  <Icon {...p}>
    <path d="M5 12h14" />
  </Icon>
);
export const RestartIcon = (p: P) => (
  <Icon {...p}>
    <path d="M4 12a8 8 0 1 0 2.5-5.8" />
    <path d="M4 4v4h4" />
  </Icon>
);
/** Final approach: a descending path toward a runway. */
export const ApproachIcon = (p: P) => (
  <Icon {...p}>
    <path d="M3 6l11 8" />
    <path d="M12 19h9" />
    <path d="M15 21l3-4" />
  </Icon>
);
export const HomeIcon = (p: P) => (
  <Icon {...p}>
    <path d="M4 11l8-6 8 6v8a1 1 0 0 1-1 1h-4v-5H9v5H5a1 1 0 0 1-1-1z" />
  </Icon>
);
export const ClipboardIcon = (p: P) => (
  <Icon {...p}>
    <rect x="6" y="5" width="12" height="16" rx="2" />
    <path d="M9 5V3.5h6V5M9 11h6M9 15h4" />
  </Icon>
);
export const ControllerIcon = (p: P) => (
  <Icon {...p}>
    <rect x="4" y="9" width="16" height="9" rx="2" />
    <path d="M8 9V6h8v3M8 13.5h3M14.5 13.5h1.5" />
  </Icon>
);
export const ChevronIcon = (p: P) => (
  <Icon {...p}>
    <path d="M9 6l6 6-6 6" />
  </Icon>
);
export const PlaneIcon = (p: P) => (
  <Icon {...p}>
    <path d="M12 3v18M4 11l8-2 8 2M9 19l3-1 3 1" />
  </Icon>
);
