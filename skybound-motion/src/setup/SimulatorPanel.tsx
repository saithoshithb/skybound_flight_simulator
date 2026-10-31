import { useReducer } from 'react';
import { useHubStatus } from '../sensor/hooks';
import { sensorHub } from '../sensor/sensorHub';
import { SimulatedTransport, simParams, type SimMotion } from '../sensor/transport/simulated';
import { Button, Panel, Segmented, SliderRow } from '../ui/primitives';

/** Drives the simulated controller and injects faults to exercise the pipeline. */
export function SimulatorPanel() {
  const status = useHubStatus();
  const [, rerender] = useReducer((x: number) => x + 1, 0);
  const set = (fn: () => void) => {
    fn();
    rerender();
  };
  const sim = sensorHub.activeTransport();
  const isSim = sim instanceof SimulatedTransport;

  return (
    <Panel title="Simulated controller" aside={<span className="text-[11px] text-ink-3">no hardware needed</span>}>
      <div className="space-y-4">
        <Segmented<SimMotion>
          label="Simulated motion"
          value={simParams.motion}
          onChange={(v) => set(() => (simParams.motion = v))}
          options={[
            { value: 'wave', label: 'Sweep' },
            { value: 'manual', label: 'Manual' },
            { value: 'still', label: 'Still' },
          ]}
        />
        {simParams.motion === 'manual' && (
          <div className="space-y-3">
            <SliderRow label="Pitch" value={simParams.target.pitch} display={`${simParams.target.pitch.toFixed(0)}°`} min={-60} max={60} step={1} onChange={(v) => set(() => (simParams.target.pitch = v))} />
            <SliderRow label="Roll" value={simParams.target.roll} display={`${simParams.target.roll.toFixed(0)}°`} min={-80} max={80} step={1} onChange={(v) => set(() => (simParams.target.roll = v))} />
            <SliderRow label="Yaw twist" value={simParams.target.yaw} display={`${simParams.target.yaw.toFixed(0)}°`} min={-60} max={60} step={1} onChange={(v) => set(() => (simParams.target.yaw = v))} />
          </div>
        )}
        <div className="space-y-3 border-t border-line pt-3">
          <div className="label">Faults</div>
          <SliderRow label="Sensor noise" value={simParams.noiseDeg} display={`±${simParams.noiseDeg.toFixed(2)}°`} min={0} max={3} step={0.05} onChange={(v) => set(() => (simParams.noiseDeg = v))} />
          <SliderRow label="Yaw drift" value={simParams.yawDriftDps} display={`${simParams.yawDriftDps.toFixed(1)}°/s`} min={0} max={3} step={0.1} onChange={(v) => set(() => (simParams.yawDriftDps = v))} />
          <SliderRow label="Dropped packets" value={simParams.dropRate} display={`${Math.round(simParams.dropRate * 100)}%`} min={0} max={0.9} step={0.01} onChange={(v) => set(() => (simParams.dropRate = v))} />
          <SliderRow label="Corrupted lines" value={simParams.malformedRate} display={`${Math.round(simParams.malformedRate * 100)}%`} min={0} max={0.5} step={0.01} onChange={(v) => set(() => (simParams.malformedRate = v))} />
          <Button size="sm" variant="danger" className="w-full" disabled={!isSim || status.link === 'disconnected'} onClick={() => isSim && sim.unplug()}>
            Simulate cable unplug
          </Button>
        </div>
      </div>
    </Panel>
  );
}
