import { useEffect, useMemo, useRef, type ReactNode } from 'react';
import { controls } from '../control/controlLoop';
import { stallSpeedKt } from '../flight/dynamics';
import { easy, flight, pilotInfo } from '../flight/session';
import { getPanel } from '../instruments/panel';
import { sensorHub } from '../sensor/sensorHub';
import { getSettings, useSettings } from '../settings/settingsStore';
import { useUi } from '../state/uiStore';
import { CameraIcon, GaugeIcon, HelpIcon, MinusIcon, PauseIcon, PlusIcon, RecenterIcon, SettingsIcon } from '../ui/icons';
import { IconButton } from '../ui/primitives';
import { perf } from './perf';
import { drawAltitudeTape, drawAttitudeBall, drawHeadingTape, drawSpeedTape, drawVsi } from './tapes';

/**
 * Flight HUD, glass-cockpit style, arranged around the screen edges so the
 * aircraft stays clear: heading ribbon on top, airspeed tape left, altitude
 * tape and vertical speed right, attitude ball and throttle in the bottom
 * corners. Everything is drawn from live telemetry once per animation frame,
 * straight to canvases and DOM nodes, so it never re-renders React.
 */

const signed = (n: number, digits = 0) => (n >= 0 ? '+' : '−') + Math.abs(n).toFixed(digits);

/** A HiDPI canvas that tracks its CSS size. */
class HudCanvas {
  ctx: CanvasRenderingContext2D | null = null;
  w = 0;
  h = 0;
  private ro: ResizeObserver | null = null;
  attach(canvas: HTMLCanvasElement | null) {
    this.ro?.disconnect();
    this.ctx = null;
    if (!canvas) return;
    const fit = () => {
      const r = canvas.getBoundingClientRect();
      const dpr = Math.min(2, window.devicePixelRatio || 1);
      this.w = r.width;
      this.h = r.height;
      canvas.width = Math.max(1, Math.round(r.width * dpr));
      canvas.height = Math.max(1, Math.round(r.height * dpr));
      this.ctx = canvas.getContext('2d');
      this.ctx?.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    fit();
    this.ro = new ResizeObserver(fit);
    this.ro.observe(canvas);
  }
}

/** Hold-to-repeat button that presses a key, so it uses exactly the keyboard's control path. */
function HoldKey({ k, label, children, className }: { k: string; label: string; children: ReactNode; className?: string }) {
  const press = (type: 'keydown' | 'keyup') => window.dispatchEvent(new KeyboardEvent(type, { key: k, bubbles: true }));
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onPointerDown={(e) => {
        e.currentTarget.setPointerCapture(e.pointerId);
        press('keydown');
      }}
      onPointerUp={() => press('keyup')}
      onPointerCancel={() => press('keyup')}
      onLostPointerCapture={() => press('keyup')}
      className={`grid cursor-pointer place-items-center rounded-md border border-white/10 bg-white/[0.04] text-ink-2 transition-[background-color,color,transform,border-color] duration-150 select-none hover:border-accent/40 hover:text-ink active:scale-95 active:bg-accent/15 active:text-accent ${className ?? ''}`}
    >
      {children}
    </button>
  );
}

/**
 * The cockpit's instrument canvas shown on screen in the chase view. It is the
 * same canvas the 3D panel uses, so the two can never disagree.
 */
function InstrumentOverlay() {
  const host = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const canvas = getPanel().canvas;
    Object.assign(canvas.style, { width: '100%', height: 'auto', display: 'block' });
    host.current?.appendChild(canvas);
    return () => canvas.remove();
  }, []);
  return (
    <div
      ref={host}
      className="glass anim-rise pointer-events-auto hidden w-[clamp(250px,26vw,360px)] overflow-hidden rounded-xl p-1 [&>canvas]:rounded-lg md:block"
      aria-label="Flight instruments"
    />
  );
}

function KeyCard({ easyFlight }: { easyFlight: boolean }) {
  const rows: [string, string][] = easyFlight
    ? [
        ['Tilt back · ↓ S', 'Climb'],
        ['Tilt forward · ↑ W', 'Descend'],
        ['Tilt sideways · ← → A D', 'Turn'],
        ['Let go', 'Levels out'],
        ['+ − · PgUp PgDn', 'Speed · first + takes off'],
        ['Tilt sideways · Q E', 'Steer on the ground'],
        ['B', 'Brakes'],
        ['V · I', 'Cockpit · instruments'],
        ['R', 'Recenter controller'],
        ['Esc · P', 'Pause'],
        ['Backspace', 'Restart on runway'],
      ]
    : [
        ['↓ S · ↑ W', 'Pull · push'],
        ['← → · A D', 'Roll'],
        ['Q · E', 'Rudder, nose wheel'],
        ['+ − · PgUp PgDn', 'Throttle'],
        ['[ ]', 'Trim'],
        ['B', 'Brakes'],
        ['V · I', 'Cockpit · instruments'],
        ['R', 'Recenter controller'],
        ['Esc · P', 'Pause'],
        ['Backspace', 'Restart on runway'],
      ];
  return (
    <div className="glass anim-slide pointer-events-auto w-[268px] rounded-xl p-3.5 text-[12px] text-ink-2">
      <div className="label mb-2 flex items-center justify-between">
        <span>{easyFlight ? 'Easy flight controls' : 'Controls'}</span>
        <span className="text-ink-3">H</span>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        {rows.map(([k, v]) => (
          <div key={k} className="contents">
            <dt className="num text-[12.5px] text-ink">{k}</dt>
            <dd className="text-right">{v}</dd>
          </div>
        ))}
      </dl>
      {easyFlight && <p className="mt-2.5 border-t border-white/[0.06] pt-2 text-[11.5px] leading-snug text-ink-3">Bank is limited to 35°, climb and descent to 1,000 fpm, and the aircraft won’t stall.</p>}
    </div>
  );
}

export function FlightHud({ onRecenter }: { onRecenter: () => void }) {
  const el = useRef<Record<string, HTMLElement | null>>({});
  const bind = (k: string) => (e: HTMLElement | null) => {
    el.current[k] = e;
  };
  const canvases = useRef({ speed: new HudCanvas(), alt: new HudCanvas(), vsi: new HudCanvas(), hdg: new HudCanvas(), ball: new HudCanvas() });
  // Stable ref callbacks, so re-renders don't re-attach the canvases.
  const canvasRef = useMemo(() => {
    const c = canvases.current;
    return {
      speed: (n: HTMLCanvasElement | null) => c.speed.attach(n),
      alt: (n: HTMLCanvasElement | null) => c.alt.attach(n),
      vsi: (n: HTMLCanvasElement | null) => c.vsi.attach(n),
      hdg: (n: HTMLCanvasElement | null) => c.hdg.attach(n),
      ball: (n: HTMLCanvasElement | null) => c.ball.attach(n),
    };
  }, []);
  const ui = useUi();
  const easyFlight = useSettings((s) => s.easyFlight);
  const cockpit = ui.view === 'cockpit';

  useEffect(() => {
    let id = 0;
    let bandsFor = '';
    let bands = { stallKt: 50, normalMaxKt: 115, vneKt: 163 };
    const set = (k: string, text: string) => {
      const n = el.current[k];
      if (n && n.textContent !== text) n.textContent = text;
    };
    /** Show/hide; on a text change, replay the entry animation (status transitions). */
    const show = (k: string, on: boolean, text?: string, display = 'inline-flex') => {
      const n = el.current[k];
      if (!n) return;
      const want = on ? display : 'none';
      if (n.style.display !== want) n.style.display = want;
      if (text !== undefined && n.textContent !== text) {
        n.textContent = text;
        n.classList.remove('anim-fade');
        void n.offsetWidth;
        n.classList.add('anim-fade');
      }
    };
    const loop = () => {
      const t = flight.telemetry;
      const s = getSettings();
      const cv = canvases.current;

      if (bandsFor !== flight.ac.id) {
        bandsFor = flight.ac.id;
        bands = { stallKt: stallSpeedKt(flight.ac), normalMaxKt: flight.ac.easy.maxKt, vneKt: flight.ac.vneMs * 1.9438 };
      }
      const target = s.easyFlight && easy.speedTargetKt > 0 ? easy.speedTargetKt : null;
      if (cv.speed.ctx) drawSpeedTape(cv.speed.ctx, cv.speed.w, cv.speed.h, t.airspeedKt, target, bands, t.phase === 'airborne');
      if (cv.alt.ctx) drawAltitudeTape(cv.alt.ctx, cv.alt.w, cv.alt.h, t.altitudeFt, t.altitudeFt - t.aglFt);
      if (cv.vsi.ctx) drawVsi(cv.vsi.ctx, cv.vsi.w, cv.vsi.h, t.verticalSpeedFpm, s.easyFlight && t.phase === 'airborne' ? pilotInfo.targetVsFpm : null);
      if (cv.hdg.ctx) drawHeadingTape(cv.hdg.ctx, cv.hdg.w, cv.hdg.h, t.headingDeg);
      if (cv.ball.ctx) drawAttitudeBall(cv.ball.ctx, cv.ball.w, t.pitchDeg, t.rollDeg);

      set('hdg', String(Math.round(t.headingDeg) % 360 || 360).padStart(3, '0') + '°');
      set('vs', signed(Math.round(t.verticalSpeedFpm / 10) * 10));
      set('agl', `${Math.max(0, Math.round(t.aglFt)).toLocaleString('en-US')} AGL`);
      set('gs', `GS ${Math.round(t.groundSpeedKt)}`);
      set('speedSet', target !== null ? `SET ${Math.round(target)}` : s.easyFlight ? 'IDLE' : `GS ${Math.round(t.groundSpeedKt)}`);
      set('pitch', signed(t.pitchDeg, 1) + '°');
      set('roll', signed(t.rollDeg, 1) + '°');
      set('aoa', t.aoaDeg.toFixed(1) + '°');
      set('g', t.loadFactor.toFixed(2));

      // Throttle lever: fill = engine power actually produced, knob = commanded throttle.
      set('thr', `${Math.round(t.throttle * 100)}`);
      const fill = el.current.thrFill;
      if (fill) fill.style.transform = `scaleY(${t.engine})`;
      const knob = el.current.thrKnob;
      if (knob) knob.style.bottom = `calc(${t.throttle * 100}% - 7px)`;
      set('thrMode', s.easyFlight ? (target !== null ? `SPEED ${Math.round(target)} KT` : 'IDLE · PRESS +') : t.trim === 0 ? 'TRIM 0' : `TRIM ${signed(t.trim * 100)}`);

      show('easy', s.easyFlight, 'EASY FLIGHT');
      show('fps', s.showFps, perf.fps > 0 ? `${Math.round(perf.fps)} FPS` : '– FPS');
      show('phase', t.phase !== 'crashed', t.phase === 'ground' ? 'ON GROUND' : 'AIRBORNE');
      show('brakes', t.brake > 0 && t.phase === 'ground', 'BRAKES');
      const warning = t.stalled ? 'STALL' : t.overspeed ? 'OVERSPEED' : t.tailStrike ? 'TAIL STRIKE' : null;
      show('warn', warning !== null, warning ?? undefined);

      const motion = pilotInfo.source === 'motion';
      // Name the real source: the simulator and the ESP32 must never look alike.
      const name = sensorHub.getStatus().transport === 'simulated' ? 'Simulated controller' : 'ESP32 controller';
      set('source', motion ? name : controls.engagement > 0 ? name + ' · engaging' : 'Keyboard');
      const dot = el.current.sourceDot;
      if (dot) dot.style.background = motion ? 'var(--color-ok)' : 'var(--color-ink-3)';
      show('recenter', motion, undefined, 'grid');
      // What the controller is asking for right now: makes an off-centre neutral obvious.
      const pct = (v: number) => Math.round(Math.abs(v) * 100);
      const roll = controls.aileron;
      const pitchCmd = controls.elevator;
      set('cmdTurn', Math.abs(roll) < 0.02 ? 'level' : `${roll > 0 ? 'R' : 'L'} ${pct(roll)}%`);
      set('cmdClimb', Math.abs(pitchCmd) < 0.02 ? 'level' : `${pitchCmd > 0 ? 'up' : 'down'} ${pct(pitchCmd)}%`);
      show('cmd', motion, undefined, 'flex');

      show('msg', performance.now() < pilotInfo.messageUntil, pilotInfo.message);
      show('crash', t.phase === 'crashed', undefined, 'grid');
      set('crashReason', t.crashReason ?? '');
      id = requestAnimationFrame(loop);
    };
    id = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(id);
  }, []);

  const restart = () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true }));

  return (
    <div className="hud-text pointer-events-none absolute inset-0 select-none">
      {/* ---------- top-left: controller and status ---------- */}
      <div className="anim-rise absolute top-3 left-3 flex max-w-[46vw] flex-col gap-1.5 sm:top-4 sm:left-4">
        <div className="glass flex w-fit items-center gap-2 rounded-lg px-3 py-1.5 text-[12px] text-ink-2">
          <span ref={bind('sourceDot')} className="inline-block size-2 rounded-full shadow-[0_0_6px_currentColor]" />
          <span ref={bind('source')} className="font-medium" />
        </div>
        <div ref={bind('cmd')} className="num glass hidden w-fit gap-3 rounded-lg px-3 py-1 text-[12.5px] text-ink-2">
          <span>
            TURN <span ref={bind('cmdTurn')} className="text-ink" />
          </span>
          <span>
            CLIMB <span ref={bind('cmdClimb')} className="text-ink" />
          </span>
        </div>
        <div className="flex flex-wrap gap-1.5 font-[family-name:var(--font-display)] text-[11.5px] font-semibold tracking-[0.14em]">
          <span ref={bind('easy')} className="hidden rounded-md border border-accent/40 bg-accent/10 px-2 py-0.5 text-accent" />
          <span ref={bind('phase')} className="glass hidden rounded-md px-2 py-0.5 text-ink-2" />
          <span ref={bind('brakes')} className="hidden rounded-md border border-warn/50 bg-warn/10 px-2 py-0.5 text-warn" />
          <span ref={bind('fps')} className="glass num hidden rounded-md px-2 py-0.5 text-ink-3" />
        </div>
      </div>

      {/* ---------- top-right: toolbar ---------- */}
      <div className="pointer-events-auto absolute top-3 right-3 flex flex-col items-end gap-2 sm:top-4 sm:right-4">
        <div className="anim-rise flex gap-1.5" role="toolbar" aria-label="Flight controls">
          <span ref={bind('recenter')} className="hidden">
            <IconButton label="Recenter controller" shortcut="R" onClick={onRecenter}>
              <RecenterIcon />
            </IconButton>
          </span>
          <IconButton label={cockpit ? 'Chase view' : 'Cockpit view'} shortcut="V" active={cockpit} onClick={ui.toggleView}>
            <CameraIcon />
          </IconButton>
          <IconButton label="Instruments" shortcut="I" active={ui.showInstruments} onClick={ui.toggleInstruments}>
            <GaugeIcon />
          </IconButton>
          <IconButton label="Controls" shortcut="H" active={ui.showHelp} onClick={ui.toggleHelp}>
            <HelpIcon />
          </IconButton>
          <IconButton label="Settings" onClick={() => ui.setShowSettings(true)}>
            <SettingsIcon />
          </IconButton>
          <IconButton label="Pause" shortcut="Esc" onClick={() => ui.setPaused(true)}>
            <PauseIcon />
          </IconButton>
        </div>
      </div>

      {/* Key card: left of the altitude tape so it never covers the instruments */}
      {ui.showHelp && (
        <div className="pointer-events-auto absolute top-28 right-[132px] hidden sm:block">
          <KeyCard easyFlight={easyFlight} />
        </div>
      )}

      {/* ---------- top-centre: heading, warnings, messages ---------- */}
      <div className="absolute top-2 left-1/2 flex w-[clamp(220px,34vw,460px)] -translate-x-1/2 flex-col items-center sm:top-3">
        <div className="relative w-full">
          <canvas ref={canvasRef.hdg} className="block h-[46px] w-full" />
        </div>
        <span ref={bind('hdg')} className="num mt-0.5 rounded border border-accent/60 bg-[rgb(6_11_19/0.86)] px-2 text-[17px] font-semibold text-ink" />
        <span
          ref={bind('warn')}
          role="alert"
          className="anim-pulse mt-2 hidden rounded-md border border-bad/70 bg-bad/20 px-3 py-0.5 font-[family-name:var(--font-display)] text-[15px] font-semibold tracking-[0.22em] text-[#ffd0cc]"
        />
        <span ref={bind('msg')} className="glass mt-2 hidden max-w-[min(560px,80vw)] rounded-lg px-3 py-1.5 text-center text-[12.5px] text-ink" />
      </div>

      {/* ---------- left edge: airspeed tape ---------- */}
      {!cockpit && (
        <div className="anim-fade absolute top-1/2 left-2 flex -translate-y-[60%] flex-col items-start sm:left-5">
          <span className="label mb-1 pl-1 !text-[10.5px]">IAS · KT</span>
          <canvas ref={canvasRef.speed} className="block h-[clamp(160px,34vh,340px)] w-[84px]" />
          <span ref={bind('speedSet')} className="num mt-1 pl-1 text-[13px] font-semibold text-accent" />
        </div>
      )}

      {/* ---------- right edge: altitude tape + vertical speed ---------- */}
      {!cockpit && (
        <div className="anim-fade absolute top-1/2 right-2 flex -translate-y-[60%] flex-col items-end sm:right-5">
          <span className="label mb-1 pr-1 !text-[10.5px]">ALT · FT</span>
          <div className="flex items-stretch gap-1">
            <canvas ref={canvasRef.vsi} className="block h-[clamp(160px,34vh,340px)] w-[16px]" />
            <canvas ref={canvasRef.alt} className="block h-[clamp(160px,34vh,340px)] w-[96px]" />
          </div>
          <div className="num mt-1 flex items-baseline gap-2 pr-1 text-[13px]">
            <span className="text-ink-3">VS</span>
            <span ref={bind('vs')} className="font-semibold text-ink" />
          </div>
          <span ref={bind('agl')} className="num pr-1 text-[12px] text-ink-3 [@media(max-height:620px)]:hidden" />
        </div>
      )}

      {/* ---------- bottom-left: attitude ---------- */}
      {!cockpit && (
        <div className="anim-rise absolute bottom-3 left-3 hidden items-end gap-3 sm:flex sm:bottom-4 sm:left-4">
          <canvas ref={canvasRef.ball} className="block size-[clamp(84px,11vw,112px)] rounded-full" />
          <dl className="num grid grid-cols-[auto_auto] gap-x-3 gap-y-0.5 pb-1 text-[13px]">
            <dt className="text-ink-3">PITCH</dt>
            <dd ref={bind('pitch')} className="text-right text-ink" />
            <dt className="text-ink-3">BANK</dt>
            <dd ref={bind('roll')} className="text-right text-ink" />
            <dt className="text-ink-3">AOA</dt>
            <dd ref={bind('aoa')} className="text-right text-ink-2" />
            <dt className="text-ink-3">G</dt>
            <dd ref={bind('g')} className="text-right text-ink-2" />
          </dl>
        </div>
      )}

      {/* ---------- bottom-centre: optional instrument panel ---------- */}
      {ui.showInstruments && !cockpit && (
        <div className="absolute bottom-3 left-1/2 -translate-x-1/2 sm:bottom-4">
          <InstrumentOverlay />
        </div>
      )}

      {/* ---------- bottom-right: throttle ---------- */}
      <div className="glass anim-rise pointer-events-auto absolute right-3 bottom-3 flex items-stretch gap-3 rounded-xl p-2.5 sm:right-4 sm:bottom-4">
        <div className="flex flex-col items-center gap-1.5">
          <HoldKey k="=" label={easyFlight ? 'Faster (hold)' : 'More throttle (hold)'} className="size-8">
            <PlusIcon size={16} />
          </HoldKey>
          <HoldKey k="-" label={easyFlight ? 'Slower (hold)' : 'Less throttle (hold)'} className="size-8">
            <MinusIcon size={16} />
          </HoldKey>
          <HoldKey k="b" label="Brakes (hold)" className="h-8 w-8 font-[family-name:var(--font-display)] text-[11px] font-semibold tracking-[0.1em]">
            BRK
          </HoldKey>
        </div>
        <div className="relative w-3 rounded-full bg-white/[0.06]" aria-hidden>
          <div ref={bind('thrFill')} className="absolute inset-0 origin-bottom rounded-full bg-[linear-gradient(to_top,var(--color-blue),var(--color-accent))] opacity-80" style={{ transform: 'scaleY(0)' }} />
          <div ref={bind('thrKnob')} className="absolute left-1/2 h-3.5 w-6 -translate-x-1/2 rounded-sm border border-white/70 bg-ink shadow" style={{ bottom: '-7px' }} />
        </div>
        <div className="flex min-w-[64px] flex-col justify-between">
          <span className="label !text-[10.5px]">Throttle</span>
          <div className="num leading-none">
            <span ref={bind('thr')} className="text-[30px] font-semibold text-ink">
              0
            </span>
            <span className="ml-0.5 text-[13px] text-ink-3">%</span>
          </div>
          <span ref={bind('thrMode')} className="num text-[11.5px] font-semibold tracking-[0.06em] text-accent" />
        </div>
      </div>

      {/* ---------- crash ---------- */}
      <div ref={bind('crash')} className="anim-fade pointer-events-auto absolute inset-0 z-10 place-items-center bg-[rgb(4_7_12/0.55)] backdrop-blur-[3px]" style={{ display: 'none' }}>
        <div className="glass-strong anim-scale w-[min(360px,90vw)] rounded-2xl p-6 text-center">
          <div className="mx-auto mb-3 grid size-10 place-items-center rounded-full border border-bad/50 bg-bad/10 text-bad">!</div>
          <div className="font-[family-name:var(--font-display)] text-[18px] font-semibold tracking-[0.2em] text-[#ffd0cc]">CRASHED</div>
          <p ref={bind('crashReason')} className="mt-2 text-[14px] text-ink" />
          <div className="mt-5 grid grid-cols-2 gap-2">
            <button
              onClick={restart}
              className="cursor-pointer rounded-md bg-accent px-3 py-2 text-[13px] font-semibold text-[#04121c] transition-colors hover:bg-[#7be2ff]"
            >
              Restart
            </button>
            <button
              onClick={() => ui.setScreen('menu')}
              className="cursor-pointer rounded-md border border-line px-3 py-2 text-[13px] font-semibold text-ink-2 transition-colors hover:border-accent/50 hover:text-ink"
            >
              Main menu
            </button>
          </div>
          <p className="mt-3 text-[11.5px] text-ink-3">Backspace to restart · Esc for the pause menu</p>
        </div>
      </div>
    </div>
  );
}
