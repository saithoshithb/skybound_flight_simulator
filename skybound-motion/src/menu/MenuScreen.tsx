import { useEffect } from 'react';
import { AIRCRAFT, type AircraftDef } from '../flight/aircraft';
import { stallSpeedKt } from '../flight/dynamics';
import { cn } from '../lib/utils';
import { useHubStatus } from '../sensor/hooks';
import { useSettings } from '../settings/settingsStore';
import { useUi } from '../state/uiStore';
import { Button } from '../ui/primitives';
import { SettingsDialog } from './SettingsDialog';

/** Side-view silhouette: the wing sits above or below the fuselage. */
function Silhouette({ ac }: { ac: AircraftDef }) {
  const high = ac.look.wing === 'high';
  return (
    <svg viewBox="0 0 220 70" className="h-[64px] w-full" aria-hidden>
      {/* fuselage */}
      <path
        d="M14 38 C 20 30, 40 28, 64 28 L 150 31 C 175 32, 192 33, 204 30 L 206 42 C 190 42, 170 43, 150 43 L 64 46 C 40 46, 20 45, 14 38 Z"
        fill={ac.look.body}
      />
      <path d="M70 37 L 196 36" stroke={ac.look.stripe} strokeWidth="2.5" />
      {/* canopy / cabin */}
      {high ? <path d="M66 28 L 78 18 L 112 18 L 116 28 Z" fill="#26323c" /> : <path d="M70 29 C 76 18, 100 16, 112 28 Z" fill="#26323c" />}
      {/* wing (seen edge-on) */}
      <rect x="72" y={high ? 14 : 44} width="56" height="5" rx="2" fill={ac.look.body} />
      {high && <path d="M84 44 L 96 19" stroke={ac.look.body} strokeWidth="2" />}
      {/* tail */}
      <path d="M182 31 L 196 8 L 208 8 L 204 31 Z" fill={ac.look.stripe} />
      <rect x="180" y="33" width="28" height="3" fill={ac.look.body} />
      {/* prop and spinner */}
      <path d="M8 38 L 14 34 L 14 42 Z" fill={ac.look.accent} />
      <rect x="6" y="18" width="2.5" height="40" rx="1" fill="#3a3d40" />
      {/* gear */}
      <path d="M40 46 L 40 58 M 104 46 L 110 58" stroke="#8e9398" strokeWidth="2" />
      <circle cx="40" cy="60" r="4" fill="#1c1e20" />
      <circle cx="110" cy="60" r="4.5" fill="#1c1e20" />
    </svg>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="label !text-[10px]">{label}</div>
      <div className="num mt-0.5 text-[14px] text-ink">{value}</div>
    </div>
  );
}

function AircraftCard({ ac, selected, onSelect }: { ac: AircraftDef; selected: boolean; onSelect: () => void }) {
  return (
    <button
      onClick={onSelect}
      aria-pressed={selected}
      className={cn(
        'group w-full cursor-pointer rounded-xl p-4 text-left transition-[border-color,background-color,transform,box-shadow] duration-200 hover:-translate-y-0.5',
        selected ? 'glass border-accent/60 shadow-[0_0_0_1px_rgb(79_214_255/0.25),0_18px_40px_-20px_rgb(79_214_255/0.45)]' : 'glass hover:border-accent/30',
      )}
    >
      <div className="flex items-baseline justify-between">
        <h3 className="font-[family-name:var(--font-display)] text-[18px] font-semibold tracking-[0.06em]">{ac.name}</h3>
        <span className={cn('text-[11px] font-semibold tracking-[0.12em]', selected ? 'text-accent' : 'text-ink-3')}>
          {selected ? 'SELECTED' : 'SELECT'}
        </span>
      </div>
      <Silhouette ac={ac} />
      <p className="text-[12.5px] leading-snug text-ink-2">{ac.description}</p>
      <div className="mt-3 grid grid-cols-4 gap-2 border-t border-line pt-3">
        <Stat label="Cruise" value={`${ac.easy.cruiseKt} kt`} />
        <Stat label="Stall" value={`${Math.round(stallSpeedKt(ac))} kt`} />
        <Stat label="Power" value={`${Math.round(ac.maxPowerW / 745.7)} hp`} />
        <Stat label="Span" value={`${ac.span.toFixed(1)} m`} />
      </div>
    </button>
  );
}

export function MenuScreen() {
  const aircraftId = useSettings((s) => s.aircraftId);
  const setAircraftId = useSettings((s) => s.setAircraftId);
  const easyFlight = useSettings((s) => s.easyFlight);
  const status = useHubStatus();
  const ui = useUi();
  const live = status.link === 'live' && status.device === 'ready';
  const controllerText = !live
    ? 'No controller connected: you can fly with the keyboard'
    : status.transport === 'simulated'
      ? 'Simulated controller connected, not your ESP32'
      : 'ESP32 motion controller connected';

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (ui.showSettings) return;
      if (e.key === 'Enter') ui.startFlight('runway');
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  return (
    <div className="relative min-h-full overflow-hidden">
      {/* Quiet sky-to-ground backdrop */}
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_at_70%_-10%,#16304f_0%,transparent_55%),linear-gradient(180deg,#0b1626_0%,#0a1220_50%,#080d16_75%,#070b12_100%)]" />
      <div className="pointer-events-none absolute inset-x-0 top-[58%] h-px bg-[linear-gradient(90deg,transparent,#4fd6ff_30%,#3d7bff_70%,transparent)] opacity-25" />

      <div className="relative mx-auto flex min-h-full max-w-[1120px] flex-col px-5 py-8 md:px-8 md:py-12">
        <header className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="anim-rise font-[family-name:var(--font-display)] text-[34px] font-semibold tracking-[0.34em] md:text-[44px]">SKYBOUND</h1>
            <p className="mt-1 text-[14px] text-ink-2">A motion-controlled flight simulator</p>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="ghost" onClick={() => ui.setScreen('setup')}>
              Controller setup
            </Button>
            <Button onClick={() => ui.setShowSettings(true)}>Settings</Button>
          </div>
        </header>

        <main className="mt-10 grid flex-1 gap-8 lg:grid-cols-[minmax(0,1fr)_320px]">
          <section>
            <h2 className="label mb-3">Aircraft</h2>
            <div className="grid gap-3 md:grid-cols-2">
              {AIRCRAFT.map((ac) => (
                <AircraftCard key={ac.id} ac={ac} selected={ac.id === aircraftId} onSelect={() => setAircraftId(ac.id)} />
              ))}
            </div>
          </section>

          <aside className="space-y-4">
            <div className="glass anim-rise rounded-xl p-4">
              <h2 className="label mb-3">Fly</h2>
              <div className="grid gap-2">
                <Button variant="primary" size="lg" onClick={() => ui.startFlight('runway')}>
                  Take off from runway 36
                </Button>
                <Button size="lg" onClick={() => ui.startFlight('final')}>
                  Start on final approach
                </Button>
              </div>
              <p className="mt-3 text-[12px] leading-snug text-ink-3">
                {easyFlight
                  ? 'Easy flight is on: press + once on the runway to take off, tilt to turn and climb, let go to level out.'
                  : 'Manual flying: full power with +, rotate at about 55 kt. Easy flight can be turned on in Settings.'}
              </p>
            </div>

            <div className="glass anim-rise rounded-xl p-4">
              <h2 className="label mb-2">Controller</h2>
              <div className="flex items-center gap-2 text-[13px] text-ink-2">
                <span className={cn('inline-block size-2 rounded-full', live ? (status.transport === 'simulated' ? 'bg-warn' : 'bg-ok') : 'bg-ink-3')} />
                {controllerText}
              </div>
              <Button size="sm" variant="ghost" className="mt-2 -ml-2" onClick={() => ui.setScreen('setup')}>
                {live ? 'Recalibrate or tune →' : 'Connect a controller →'}
              </Button>
            </div>
          </aside>
        </main>

        <footer className="mt-10 flex flex-wrap gap-x-6 gap-y-1 text-[11.5px] text-ink-3">
          <span>Enter · take off</span>
          <span>In flight: Esc pause · V cockpit · I instruments · R recenter</span>
        </footer>
      </div>

      {ui.showSettings && <SettingsDialog />}
    </div>
  );
}
