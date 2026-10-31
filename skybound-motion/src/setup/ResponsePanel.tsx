import { shapeAxis, type YawMode } from '../control/mapping';
import { useSettings } from '../settings/settingsStore';
import { Button, Panel, Segmented, SliderRow, Switch } from '../ui/primitives';

/** Plot of controller tilt → command for the roll axis with current settings. */
function CurvePreview() {
  const c = useSettings((s) => s.control);
  const W = 220;
  const H = 120;
  const span = c.roll.maxAngleDeg * 1.25;
  const pts: string[] = [];
  for (let i = 0; i <= 80; i++) {
    const deg = -span + (2 * span * i) / 80;
    const y = shapeAxis(deg, { ...c.roll, invert: false }, c);
    pts.push(`${((i / 80) * W).toFixed(1)},${(H / 2 - (y * H) / 2.2).toFixed(1)}`);
  }
  const xAt = (deg: number) => ((deg + span) / (2 * span)) * W;
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="h-[120px] w-full" role="img" aria-label="Roll response curve">
      <line x1={0} x2={W} y1={H / 2} y2={H / 2} stroke="var(--color-line)" />
      <line x1={W / 2} x2={W / 2} y1={0} y2={H} stroke="var(--color-line)" />
      {[-c.roll.maxAngleDeg, c.roll.maxAngleDeg].map((d) => (
        <line key={d} x1={xAt(d)} x2={xAt(d)} y1={0} y2={H} stroke="var(--color-line)" strokeDasharray="2 3" />
      ))}
      <polyline points={pts.join(' ')} fill="none" stroke="var(--color-accent)" strokeWidth={1.75} />
      <text x={4} y={11} fontSize={9} fill="var(--color-ink-3)">
        command
      </text>
      <text x={W - 4} y={H - 4} fontSize={9} fill="var(--color-ink-3)" textAnchor="end">
        roll tilt ±{span.toFixed(0)}°
      </text>
    </svg>
  );
}

export function ResponsePanel() {
  const c = useSettings((s) => s.control);
  const setControl = useSettings((s) => s.setControl);
  const setAxis = useSettings((s) => s.setAxis);
  const reset = useSettings((s) => s.resetControl);

  return (
    <Panel
      title="Response"
      aside={
        <Button size="sm" variant="ghost" className="h-6 px-2 text-[11.5px]" onClick={reset}>
          Defaults
        </Button>
      }
    >
      <div className="space-y-4">
        <CurvePreview />
        <SliderRow label="Sensitivity" value={c.sensitivity} display={`${c.sensitivity.toFixed(2)}×`} min={0.3} max={2} step={0.05} onChange={(v) => setControl({ sensitivity: v })} />
        <SliderRow label="Dead zone" value={c.deadzone} display={`${Math.round(c.deadzone * 100)}%`} min={0} max={0.3} step={0.01} onChange={(v) => setControl({ deadzone: v })} hint="Range around neutral that produces no command" />
        <SliderRow label="Expo (center softness)" value={c.expo} display={c.expo.toFixed(2)} min={0} max={1} step={0.05} onChange={(v) => setControl({ expo: v })} />
        <SliderRow label="Smoothing" value={c.smoothingMs} display={`${c.smoothingMs} ms`} min={0} max={250} step={5} onChange={(v) => setControl({ smoothingMs: v })} hint="Low-pass time constant: removes tremor, adds lag" />
        <SliderRow label="Max rate of change" value={c.slewPerSec} display={`${c.slewPerSec.toFixed(1)} /s`} min={1} max={12} step={0.5} onChange={(v) => setControl({ slewPerSec: v })} hint="Full-scale deflections per second" />

        <div className="space-y-3 border-t border-line pt-3">
          <div className="label">Axes</div>
          {(['pitch', 'roll'] as const).map((axis) => (
            <div key={axis} className="space-y-2">
              <SliderRow
                label={`${axis === 'pitch' ? 'Pitch' : 'Roll'} tilt for full deflection`}
                value={c[axis].maxAngleDeg}
                display={`${c[axis].maxAngleDeg}°`}
                min={10}
                max={70}
                step={1}
                onChange={(v) => setAxis(axis, { maxAngleDeg: v })}
              />
              <label className="flex items-center justify-between text-[12.5px] text-ink-2">
                Invert {axis}
                <Switch checked={c[axis].invert} onCheckedChange={(v) => setAxis(axis, { invert: v })} />
              </label>
            </div>
          ))}
        </div>

        <div className="space-y-2.5 border-t border-line pt-3">
          <div className="label">Yaw / rudder</div>
          <Segmented<YawMode>
            label="Yaw mode"
            value={c.yawMode}
            onChange={(v) => setControl({ yawMode: v })}
            options={[
              { value: 'off', label: 'Off' },
              { value: 'washout', label: 'Twist (washout)' },
              { value: 'rate', label: 'Twist rate' },
            ]}
          />
          <p className="text-[11.5px] leading-snug text-ink-3">
            {c.yawMode === 'off' && 'Recommended. The MPU-6050 has no magnetometer, so yaw angle drifts. The aircraft still turns by banking.'}
            {c.yawMode === 'washout' && 'Twisting gives rudder, which fades back to zero while held. This cancels gyro drift.'}
            {c.yawMode === 'rate' && 'Rudder follows how fast you twist. Holding still means no rudder, so drift can\'t build up.'}
          </p>
          {c.yawMode === 'washout' && (
            <SliderRow label="Washout time" value={c.yawWashoutS} display={`${c.yawWashoutS.toFixed(1)} s`} min={0.5} max={10} step={0.5} onChange={(v) => setControl({ yawWashoutS: v })} />
          )}
          {c.yawMode === 'rate' && (
            <SliderRow label="Twist rate for full rudder" value={c.yawFullRateDps} display={`${c.yawFullRateDps}°/s`} min={20} max={300} step={5} onChange={(v) => setControl({ yawFullRateDps: v })} />
          )}
          {c.yawMode !== 'off' && (
            <label className="flex items-center justify-between text-[12.5px] text-ink-2">
              Invert yaw
              <Switch checked={c.yaw.invert} onCheckedChange={(v) => setAxis('yaw', { invert: v })} />
            </label>
          )}
        </div>
      </div>
    </Panel>
  );
}
