import { create } from 'zustand';

export type Screen = 'menu' | 'setup' | 'flight';
export type CameraView = 'chase' | 'cockpit';
export type StartPosition = 'runway' | 'final';

/** Coarse UI state only: changes on user actions, never per frame. */
interface UiState {
  screen: Screen;
  /** Where the next flight starts (read when the flight screen mounts). */
  start: StartPosition;
  paused: boolean;
  view: CameraView;
  showHelp: boolean;
  /** On-screen instrument panel in the chase view. */
  showInstruments: boolean;
  /** Settings dialog, over the menu or the pause menu. */
  showSettings: boolean;
  setScreen(s: Screen): void;
  startFlight(start: StartPosition): void;
  setPaused(p: boolean): void;
  toggleView(): void;
  toggleHelp(): void;
  toggleInstruments(): void;
  setShowSettings(open: boolean): void;
}

export const useUi = create<UiState>()((set) => ({
  screen: 'menu',
  start: 'runway',
  paused: false,
  view: 'chase',
  showHelp: true,
  showInstruments: true,
  showSettings: false,
  setScreen: (screen) => set({ screen, paused: false, showSettings: false }),
  startFlight: (start) => set({ screen: 'flight', start, paused: false, showSettings: false }),
  setPaused: (paused) => set({ paused }),
  toggleView: () => set((s) => ({ view: s.view === 'chase' ? 'cockpit' : 'chase' })),
  toggleHelp: () => set((s) => ({ showHelp: !s.showHelp })),
  toggleInstruments: () => set((s) => ({ showInstruments: !s.showInstruments })),
  setShowSettings: (showSettings) => set({ showSettings }),
}));
