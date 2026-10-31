import { lazy, Suspense, useEffect } from 'react';
import { startControlLoop } from './control/controlLoop';
import { MenuScreen } from './menu/MenuScreen';
import { useUi } from './state/uiStore';

const FlightScreen = lazy(() => import('./hud/FlightScreen').then((m) => ({ default: m.FlightScreen })));
const SetupScreen = lazy(() => import('./setup/SetupScreen').then((m) => ({ default: m.SetupScreen })));

function ScreenLoading() {
  return (
    <div className="grid h-full place-items-center">
      <div className="text-center">
        <div className="text-[13px] font-bold tracking-[0.22em] text-ink-2">SKYBOUND</div>
        <div className="mt-3 text-[12.5px] text-ink-3">Loading…</div>
      </div>
    </div>
  );
}

export default function App() {
  const screen = useUi((s) => s.screen);
  useEffect(() => startControlLoop(), []);
  // The sensor link lives in SensorHub, so it stays connected across screens.
  // The 3D screens (three.js, R3F, post-processing) load on demand, so the menu appears fast.
  return (
    <Suspense fallback={<ScreenLoading />}>
      {screen === 'flight' ? <FlightScreen /> : screen === 'setup' ? <SetupScreen /> : <MenuScreen />}
    </Suspense>
  );
}
