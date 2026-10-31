import { useEffect, useState, type ReactNode } from 'react';
import { captureNeutral } from '../calibration/neutral';
import { pipeline } from '../control/controlLoop';
import { flightLogCsv } from '../flight/recorder';
import { flash, resetAirborne, resetOnRunway } from '../flight/session';
import { attachKeyboard } from '../input/keyboard';
import { useHubStatus } from '../sensor/hooks';
import { sensorHub } from '../sensor/sensorHub';
import { useSettings } from '../settings/settingsStore';
import { useUi } from '../state/uiStore';
import { ApproachIcon, CameraIcon, ClipboardIcon, HomeIcon, PlaneIcon, PlayIcon, RecenterIcon, RestartIcon, SettingsIcon } from '../ui/icons';
import { FlightScene } from '../world/FlightScene';
import { FlightHud } from './FlightHud';
import { aircraftById } from '../flight/aircraft';
import { SettingsDialog } from '../menu/SettingsDialog';

async function recenter() {
  if (!sensorHub.isUsable()) {
    flash('No live motion controller to recenter');
    return;
  }
  flash('Recentering · hold still', 1200);
  const r = await captureNeutral(sensorHub);
  if (!r.ok) return flash(r.detail, 3500);
  useSettings.getState().setNeutral(r.neutral);
  pipeline.reengage();
  flash('Neutral set');
}

/**
 * Whatever pose the pilot holds when a flight starts is "straight and level".
 * A neutral saved earlier (different grip, controller on the desk) otherwise
 * reads as a constant small turn. Skipped when no controller is live; if the
 * hand is moving, the capture is rejected and the old neutral stays.
 */
let autoRecenterRun = 0;
async function autoRecenter() {
  if (!sensorHub.isUsable()) return;
  const run = ++autoRecenterRun;
  flash('Hold the controller in your normal position · setting neutral', 2500);
  await new Promise((r) => setTimeout(r, 1200));
  if (run !== autoRecenterRun || !sensorHub.isUsable()) return;
  const r = await captureNeutral(sensorHub);
  if (run !== autoRecenterRun) return;
  if (!r.ok) {
    flash('Could not set neutral (controller moving). Hold it still and press R.', 4500);
    return;
  }
  useSettings.getState().setNeutral(r.neutral);
  pipeline.reengage();
  flash('Neutral set · this position now flies straight', 3000);
}

function restartOnRunway() {
  resetOnRunway();
  void autoRecenter();
}

function restartAirborne() {
  resetAirborne();
  void autoRecenter();
}

function MenuItem({ icon, label, hint, onClick }: { icon: ReactNode; label: string; hint?: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="group flex w-full cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-left text-[13px] text-ink-2 transition-colors duration-150 hover:bg-white/[0.05] hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
    >
      <span className="grid size-7 place-items-center rounded-md border border-white/[0.08] text-ink-3 transition-colors group-hover:border-accent/40 group-hover:text-accent">{icon}</span>
      <span className="flex-1">{label}</span>
      {hint && <span className="num text-[11.5px] text-ink-3">{hint}</span>}
    </button>
  );
}

function PauseMenu() {
  const ui = useUi();
  const hub = useHubStatus();
  const recenterAvailable = hub.link === 'live' && hub.device === 'ready';
  const resume = () => ui.setPaused(false);
  const [logCopied, setLogCopied] = useState<string | null>(null);
  const copyLog = async () => {
    const csv = flightLogCsv();
    try {
      await navigator.clipboard.writeText(csv);
      setLogCopied('Copied, paste it into the chat');
    } catch {
      // Clipboard blocked: fall back to a file download.
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
      a.download = 'skybound-flight-log.csv';
      a.click();
      URL.revokeObjectURL(a.href);
      setLogCopied('Saved skybound-flight-log.csv');
    }
  };
  return (
    <div className="anim-fade absolute inset-0 z-20 grid place-items-center bg-[rgb(4_7_12/0.5)] p-4 backdrop-blur-[4px]">
      <div className="glass-strong anim-scale w-[min(340px,100%)] rounded-2xl p-4">
        <div className="flex items-center justify-between px-1">
          <span className="font-[family-name:var(--font-display)] text-[18px] font-semibold tracking-[0.24em] text-ink">PAUSED</span>
          <span className="num text-[11.5px] text-ink-3">Esc to resume</span>
        </div>
        <button
          type="button"
          onClick={resume}
          autoFocus
          className="mt-4 flex w-full cursor-pointer items-center justify-center gap-2 rounded-lg bg-accent py-2.5 text-[14px] font-semibold text-[#04121c] shadow-[0_8px_24px_-10px_rgb(79_214_255/0.6)] transition-[background-color,transform] duration-150 hover:bg-[#7be2ff] active:scale-[0.98]"
        >
          <PlayIcon size={16} /> Resume
        </button>
        <div className="mt-3 grid gap-0.5">
          <MenuItem
            icon={<RestartIcon size={15} />}
            label="Restart on runway"
            hint="⌫"
            onClick={() => {
              restartOnRunway();
              resume();
            }}
          />
          <MenuItem
            icon={<ApproachIcon size={15} />}
            label="Start on final approach"
            onClick={() => {
              restartAirborne();
              resume();
            }}
          />
          <div className="mx-3 my-1.5 h-px bg-white/[0.06]" />
          <MenuItem icon={<CameraIcon size={15} />} label={`View · ${ui.view === 'chase' ? 'Chase' : 'Cockpit'}`} hint="V" onClick={ui.toggleView} />
          {recenterAvailable && <MenuItem icon={<RecenterIcon size={15} />} label="Recenter controller" hint="R" onClick={() => void recenter()} />}
          <MenuItem icon={<SettingsIcon size={15} />} label="Settings" onClick={() => ui.setShowSettings(true)} />
          <MenuItem icon={<ClipboardIcon size={15} />} label={logCopied ?? 'Copy flight log'} onClick={() => void copyLog()} />
          <div className="mx-3 my-1.5 h-px bg-white/[0.06]" />
          <MenuItem icon={<HomeIcon size={15} />} label="Main menu" onClick={() => ui.setScreen('menu')} />
        </div>
      </div>
    </div>
  );
}

/** Shown while the 3D world builds; covers the first frames so nothing pops in. */
function LoadingOverlay({ visible }: { visible: boolean }) {
  const aircraft = aircraftById(useSettings((s) => s.aircraftId));
  const easyOn = useSettings((s) => s.easyFlight);
  return (
    <div
      className={`absolute inset-0 z-40 grid place-items-center bg-[radial-gradient(ellipse_at_50%_35%,#0f1a2b_0%,#070b12_70%)] transition-opacity duration-700 ${visible ? 'opacity-100' : 'pointer-events-none opacity-0'}`}
      aria-hidden={!visible}
    >
      <div className="anim-rise w-[340px] text-center">
        <div className="font-[family-name:var(--font-display)] text-[15px] font-semibold tracking-[0.42em] text-ink">SKYBOUND</div>
        <div className="mx-auto mt-6 grid size-14 place-items-center rounded-full border border-accent/30 text-accent shadow-[0_0_30px_-8px_rgb(79_214_255/0.6)]">
          <PlaneIcon size={26} />
        </div>
        <div className="mt-5 text-[15px] text-ink">Preparing the airfield</div>
        <div className="num mt-1 text-[13px] tracking-[0.08em] text-ink-3">{aircraft.name.toUpperCase()} · RUNWAY 36</div>
        <div className="mx-auto mt-5 h-[2px] w-[200px] overflow-hidden rounded-full bg-white/[0.07]">
          <div className="h-full w-1/3 animate-[loading_1.1s_ease-in-out_infinite] rounded-full bg-[linear-gradient(90deg,transparent,var(--color-accent),transparent)]" />
        </div>
        <p className="mt-6 text-[12px] leading-snug text-ink-3">
          {easyOn ? 'Tip: hold the controller in your normal position as the flight starts; that pose flies straight.' : 'Tip: trim with [ and ] to fly hands-off.'}
        </p>
      </div>
    </div>
  );
}

export function FlightScreen() {
  const paused = useUi((s) => s.paused);
  const showSettings = useUi((s) => s.showSettings);
  // Mount the heavy 3D scene a frame after the loading screen paints.
  const [mountScene, setMountScene] = useState(false);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    let id = requestAnimationFrame(() => (id = requestAnimationFrame(() => setMountScene(true))));
    return () => cancelAnimationFrame(id);
  }, []);

  useEffect(() => {
    const detach = attachKeyboard();
    const onKey = (e: KeyboardEvent) => {
      if (e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
      const ui = useUi.getState();
      if (ui.showSettings) return; // the dialog handles its own keys
      switch (e.key.toLowerCase()) {
        case 'escape':
        case 'p':
          ui.setPaused(!ui.paused);
          break;
        case 'v':
          ui.toggleView();
          break;
        case 'i':
          ui.toggleInstruments();
          break;
        case 'h':
          ui.toggleHelp();
          break;
        case 'backspace':
          e.preventDefault();
          restartOnRunway();
          ui.setPaused(false);
          break;
        case 'r':
          void recenter();
          break;
      }
    };
    window.addEventListener('keydown', onKey);
    // Pause automatically when the tab is hidden so the aircraft doesn't fly unattended.
    const onVis = () => document.hidden && useUi.getState().setPaused(true);
    document.addEventListener('visibilitychange', onVis);
    // The key card gets out of the way after a while; H brings it back.
    const helpTimer = setTimeout(() => useUi.getState().showHelp && useUi.getState().toggleHelp(), 15000);
    return () => {
      clearTimeout(helpTimer);
      detach();
      window.removeEventListener('keydown', onKey);
      document.removeEventListener('visibilitychange', onVis);
    };
  }, []);

  // Start where the menu asked, once the world is on screen.
  useEffect(() => {
    if (!ready) return;
    if (useUi.getState().start === 'final') restartAirborne();
    else {
      restartOnRunway();
      flash(
        useSettings.getState().easyFlight
          ? 'Runway 36 · press + (or PgUp) once to take off'
          : 'Runway 36 · full power with + or PgUp, rotate at 55 kt',
        4000,
      );
    }
  }, [ready]);

  return (
    <div className="relative h-full w-full overflow-hidden bg-bg">
      {mountScene && <FlightScene onReady={() => setReady(true)} />}
      {ready && <FlightHud onRecenter={() => void recenter()} />}
      {paused && !showSettings && <PauseMenu />}
      {showSettings && <SettingsDialog />}
      <LoadingOverlay visible={!ready} />
    </div>
  );
}
