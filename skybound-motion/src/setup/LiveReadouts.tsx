import { useEffect, useRef } from 'react';
import { controls, pipeline } from '../control/controlLoop';
import { wrap180 } from '../control/mapping';
import { sensorHub } from '../sensor/sensorHub';
import { getSettings } from '../settings/settingsStore';
import { Panel } from '../ui/primitives';

/**
 * Everything here updates through refs inside requestAnimationFrame, so a
 * 50 Hz sensor stream causes zero React re-renders.
 */
function useFrameWriter(write: () => void) {
  const fn = useRef(write);
  fn.current = write;
  useEffect(() => {
    let id = 0;
    const loop = () => {
      fn.current();
      id = requestAnimationFrame(loop);
    };
    id = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(id);
  }, []);
}

const fmt = (v: number, digits = 1) => (v >= 0 ? '+' : '−') + Math.abs(v).toFixed(digits);

type AngleKey = 'pitch' | 'roll' | 'yaw';

function AngleReadout({ name, hint, refs }: { name: string; hint: string; refs: { abs: (el: HTMLElement | null) => void; rel: (el: HTMLElement | null) => void; rate: (el: HTMLElement | null) => void } }) {
  return (
    <div className="rounded-md border border-line bg-bg/50 px-3 py-2.5">
      <div className="flex items-baseline justify-between">
        <span className="label">{name}</span>
        <span className="text-[10.5px] text-ink-3">{hint}</span>
      </div>
      <div className="num mt-1 text-[26px] leading-none font-medium text-ink" ref={refs.rel}>
        —
      </div>
      <div className="num mt-1.5 flex justify-between text-[11.5px] text-ink-3">
        <span>
          raw <span ref={refs.abs}>—</span>
        </span>
        <span>
          <span ref={refs.rate}>—</span>°/s
        </span>
      </div>
    </div>
  );
}

function CommandBar({ name, setRefs }: { name: string; setRefs: (fill: HTMLElement | null, target: HTMLElement | null, value: HTMLElement | null) => void }) {
  const fill = useRef<HTMLDivElement>(null);
  const target = useRef<HTMLDivElement>(null);
  const value = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    setRefs(fill.current, target.current, value.current);
  });
  return (
    <div className="grid grid-cols-[64px_1fr_52px] items-center gap-3">
      <span className="text-[12px] text-ink-2">{name}</span>
      <div className="relative h-2.5 rounded-sm bg-bg">
        <div className="absolute inset-y-0 left-1/2 w-px bg-line" />
        <div ref={fill} className="absolute inset-y-0 left-1/2 w-1/2 origin-left bg-ink-2" style={{ transform: 'scaleX(0)' }} />
        <div ref={target} className="absolute -inset-y-0.5 left-1/2 w-0.5 bg-accent" />
      </div>
      <span ref={value} className="num text-right text-[12px]">
        0.00
      </span>
    </div>
  );
}

export function LiveReadouts() {
  const el = useRef<Record<string, HTMLElement | null>>({});
  const bind = (k: string) => (e: HTMLElement | null) => {
    el.current[k] = e;
  };
  const angleRefs = (k: AngleKey) => ({ abs: bind(`${k}.abs`), rel: bind(`${k}.rel`), rate: bind(`${k}.rate`) });
  const bars = useRef<Record<string, [HTMLElement | null, HTMLElement | null, HTMLElement | null]>>({});

  useFrameWriter(() => {
    const e = el.current;
    const latest = sensorHub.latest();
    const usable = sensorHub.isUsable();
    const { neutral } = getSettings();
    const set = (k: string, text: string) => {
      const n = e[k];
      if (n && n.textContent !== text) n.textContent = text;
    };
    if (latest) {
      const s = latest.sample;
      const rel = { pitch: s.pitch - neutral.pitch, roll: wrap180(s.roll - neutral.roll), yaw: wrap180(s.yaw - neutral.yaw) };
      const rate = { pitch: s.pitchRate, roll: s.rollRate, yaw: s.yawRate };
      for (const k of ['pitch', 'roll', 'yaw'] as const) {
        set(`${k}.rel`, `${fmt(rel[k])}°`);
        set(`${k}.abs`, `${fmt(s[k])}°`);
        set(`${k}.rate`, fmt(rate[k], 0));
      }
      set('accel', `${s.accelG.toFixed(2)} g`);
    } else {
      for (const k of ['pitch', 'roll', 'yaw']) for (const f of ['rel', 'abs', 'rate']) set(`${k}.${f}`, '—');
      set('accel', '—');
    }
    const wrap = e.wrap;
    if (wrap) wrap.style.opacity = usable ? '1' : '0.45';

    for (const k of ['elevator', 'aileron', 'rudder'] as const) {
      const [fill, target, value] = bars.current[k] ?? [];
      const v = controls[k];
      if (fill) fill.style.transform = `scaleX(${v})`;
      if (target) target.style.left = `${50 + pipeline.target[k] * 50}%`;
      if (value) value.textContent = fmt(v, 2);
    }
    const stick = e.stick;
    if (stick) stick.style.transform = `translate(${controls.aileron * 50}%, ${-controls.elevator * 50}%)`;
    set('engage', `${Math.round(controls.engagement * 100)}%`);
    const eng = e.engageBar;
    if (eng) eng.style.transform = `scaleX(${controls.engagement})`;
  });

  return (
    <Panel title="Live attitude & commands" aside={<span ref={bind('accel')} className="num text-[11.5px] text-ink-3" title="Accelerometer magnitude (≈1 g at rest)" />}>
      <div ref={bind('wrap')} className="space-y-4 transition-opacity">
        <div className="grid grid-cols-3 gap-2">
          <AngleReadout name="Pitch" hint="+ nose up" refs={angleRefs('pitch')} />
          <AngleReadout name="Roll" hint="+ right down" refs={angleRefs('roll')} />
          <AngleReadout name="Yaw" hint="relative, drifts" refs={angleRefs('yaw')} />
        </div>
        <div className="grid grid-cols-[1fr_auto] items-center gap-5">
          <div className="space-y-2.5">
            <CommandBar name="Elevator" setRefs={(...r) => (bars.current.elevator = r)} />
            <CommandBar name="Aileron" setRefs={(...r) => (bars.current.aileron = r)} />
            <CommandBar name="Rudder" setRefs={(...r) => (bars.current.rudder = r)} />
            <div className="grid grid-cols-[64px_1fr_52px] items-center gap-3 pt-1">
              <span className="text-[12px] text-ink-3">Authority</span>
              <div className="h-1 rounded-sm bg-bg">
                <div ref={bind('engageBar')} className="h-full origin-left bg-ok/70" style={{ transform: 'scaleX(0)' }} />
              </div>
              <span ref={bind('engage')} className="num text-right text-[12px] text-ink-3">
                0%
              </span>
            </div>
          </div>
          {/* Virtual stick: where the pilot's "yoke" is after all shaping */}
          <div className="relative size-[104px] rounded-md border border-line bg-bg" aria-label="Virtual stick position">
            <div className="absolute inset-x-0 top-1/2 h-px bg-line" />
            <div className="absolute inset-y-0 left-1/2 w-px bg-line" />
            <div className="absolute inset-0 grid place-items-center">
              <div ref={bind('stick')} className="size-full">
                <div className="absolute top-1/2 left-1/2 size-3 -translate-1/2 rounded-full border-2 border-accent bg-panel" />
              </div>
            </div>
          </div>
        </div>
        <p className="text-[11.5px] leading-snug text-ink-3">
          Big numbers are relative to neutral. Grey bars are the shaped command sent to the aircraft; the amber tick is the unsmoothed target.
        </p>
      </div>
    </Panel>
  );
}
