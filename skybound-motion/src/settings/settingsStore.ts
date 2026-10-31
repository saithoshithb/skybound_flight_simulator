import { create } from 'zustand';
import { createJSONStorage, persist, type StateStorage } from 'zustand/middleware';
import { ZERO_NEUTRAL, type Neutral } from '../calibration/neutral';
import { DEFAULT_CONTROL_SETTINGS, PREVIOUS_SHAPING_DEFAULTS, type AxisSettings, type ControlSettings } from '../control/mapping';
import type { TransportKind } from '../sensor/transport/types';

export type GraphicsQuality = 'low' | 'medium' | 'high';

interface SettingsState {
  /** Selected aircraft (see flight/aircraft.ts). */
  aircraftId: string;
  setAircraftId(id: string): void;
  graphics: GraphicsQuality;
  setGraphics(q: GraphicsQuality): void;
  /** Bloom and vignette (High quality only). */
  postFx: boolean;
  setPostFx(on: boolean): void;
  showFps: boolean;
  setShowFps(on: boolean): void;
  control: ControlSettings;
  neutral: Neutral;
  source: TransportKind;
  /** Flight assist: automatic rudder coordination while airborne. */
  autoRudder: boolean;
  setAutoRudder(on: boolean): void;
  /** Flight assist: roll input sets a bank angle instead of a roll rate. */
  bankAssist: boolean;
  setBankAssist(on: boolean): void;
  /**
   * Easy flight: stabilised, forgiving flying for first-time pilots — tilt sets
   * bank and climb rate, auto-throttle holds speed, limits prevent upsets.
   * Includes bank assist and auto-rudder regardless of those toggles.
   */
  easyFlight: boolean;
  setEasyFlight(on: boolean): void;
  setControl(patch: Partial<ControlSettings>): void;
  setAxis(axis: 'pitch' | 'roll' | 'yaw', patch: Partial<AxisSettings>): void;
  setNeutral(n: Neutral): void;
  setSource(s: TransportKind): void;
  resetControl(): void;
}

// Storage can throw (private mode, blocked site data); settings then live in memory only.
const safeStorage: StateStorage = {
  getItem: (k) => {
    try {
      return localStorage.getItem(k);
    } catch {
      return null;
    }
  },
  setItem: (k, v) => {
    try {
      localStorage.setItem(k, v);
    } catch {
      /* not persisted */
    }
  },
  removeItem: (k) => {
    try {
      localStorage.removeItem(k);
    } catch {
      /* ignore */
    }
  },
};

export const useSettings = create<SettingsState>()(
  persist(
    (set) => ({
      control: DEFAULT_CONTROL_SETTINGS,
      neutral: ZERO_NEUTRAL,
      source: 'simulated',
      autoRudder: true,
      setAutoRudder: (autoRudder) => set({ autoRudder }),
      bankAssist: true,
      setBankAssist: (bankAssist) => set({ bankAssist }),
      aircraftId: 'trainer',
      setAircraftId: (aircraftId) => set({ aircraftId }),
      graphics: 'medium',
      setGraphics: (graphics) => set({ graphics }),
      postFx: true,
      setPostFx: (postFx) => set({ postFx }),
      showFps: false,
      setShowFps: (showFps) => set({ showFps }),
      easyFlight: true,
      setEasyFlight: (easyFlight) => set({ easyFlight }),
      setControl: (patch) => set((s) => ({ control: { ...s.control, ...patch } })),
      setAxis: (axis, patch) => set((s) => ({ control: { ...s.control, [axis]: { ...s.control[axis], ...patch } } })),
      setNeutral: (neutral) => set({ neutral }),
      setSource: (source) => set({ source }),
      resetControl: () => set({ control: DEFAULT_CONTROL_SETTINGS }),
    }),
    {
      name: 'skybound-motion/settings',
      version: 2,
      storage: createJSONStorage(() => safeStorage),
      partialize: ({ control, neutral, source, autoRudder, bankAssist, easyFlight, aircraftId, graphics, postFx, showFps }) => ({
        aircraftId,
        graphics,
        postFx,
        showFps,
        control,
        neutral,
        source,
        autoRudder,
        bankAssist,
        easyFlight,
      }),
      // v2 (Easy flight): move response-shaping values the user never changed to the new defaults.
      migrate: (persisted, version) => {
        const p = (persisted ?? {}) as Partial<SettingsState>;
        if (version < 2 && p.control) {
          const c = { ...p.control };
          for (const k of Object.keys(PREVIOUS_SHAPING_DEFAULTS) as (keyof typeof PREVIOUS_SHAPING_DEFAULTS)[])
            if (c[k] === PREVIOUS_SHAPING_DEFAULTS[k]) c[k] = DEFAULT_CONTROL_SETTINGS[k];
          p.control = c;
        }
        return p as SettingsState;
      },
      // Fill in fields added after a user's settings were saved.
      merge: (persisted, current) => {
        const p = (persisted ?? {}) as Partial<SettingsState>;
        return {
          ...current,
          ...p,
          control: { ...DEFAULT_CONTROL_SETTINGS, ...p.control },
          neutral: { ...ZERO_NEUTRAL, ...p.neutral },
        };
      },
    },
  ),
);

/** Non-reactive read for render loops. */
export const getSettings = () => useSettings.getState();
