import { useHubStatus } from '../sensor/hooks';
import { isUsableStatus } from '../sensor/sensorHub';
import { useSettings } from '../settings/settingsStore';
import { useUi } from '../state/uiStore';
import { Button } from '../ui/primitives';
import { ConnectionPanel } from './ConnectionPanel';
import { ControllerView } from './ControllerView';
import { DiagnosticsPanel } from './DiagnosticsPanel';
import { LiveReadouts } from './LiveReadouts';
import { ResponsePanel } from './ResponsePanel';
import { SimulatorPanel } from './SimulatorPanel';

/** Motion-controller connection, calibration and tuning. */
export function SetupScreen() {
  const source = useSettings((s) => s.source);
  const neutralSet = useSettings((s) => s.neutral.capturedAt > 0);
  const status = useHubStatus();
  const setScreen = useUi((s) => s.setScreen);
  const startFlight = useUi((s) => s.startFlight);
  const live = isUsableStatus(status);

  return (
    <div className="mx-auto flex min-h-full max-w-[1480px] flex-col gap-4 px-4 py-4 md:px-6">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-line pb-3">
        <div className="flex items-baseline gap-3">
          <h1 className="text-[15px] font-bold tracking-[0.18em]">SKYBOUND</h1>
          <span className="text-[13px] text-ink-3">Motion controller setup</span>
        </div>
        <div className="flex items-center gap-3">
          <span className="text-[12px] text-ink-3">
            {live
              ? status.transport === 'simulated'
                ? 'Simulator connected, not your ESP32 (switch Source to ESP32 · USB)'
                : neutralSet
                  ? 'ESP32 controller ready'
                  : 'Set neutral with Recenter before flying'
              : 'No controller: you can fly with the keyboard'}
          </span>
          <Button variant="ghost" onClick={() => setScreen('menu')}>
            ← Menu
          </Button>
          <Button variant="primary" onClick={() => startFlight('runway')}>
            Fly →
          </Button>
        </div>
      </header>

      <main className="grid flex-1 gap-4 lg:grid-cols-[300px_minmax(0,1fr)_300px]">
        <div className="space-y-4">
          <ConnectionPanel />
          {source === 'simulated' && <SimulatorPanel />}
        </div>
        <div className="flex min-w-0 flex-col gap-4">
          <div className="h-[340px] lg:h-auto lg:min-h-[340px] lg:flex-1">
            <ControllerView />
          </div>
          <LiveReadouts />
          <DiagnosticsPanel />
        </div>
        <div>
          <ResponsePanel />
        </div>
      </main>
    </div>
  );
}
