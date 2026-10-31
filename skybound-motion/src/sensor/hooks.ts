import { useEffect, useState, useSyncExternalStore } from 'react';
import { sensorHub, type HubStats, type HubStatus } from './sensorHub';

/** Coarse link status. Re-renders only when the status actually changes. */
export function useHubStatus(): HubStatus {
  return useSyncExternalStore(
    (cb) => sensorHub.subscribe(cb),
    () => sensorHub.getStatus(),
  );
}

/** Diagnostics counters, polled at a human-readable rate (not sensor rate). */
export function useHubStats(intervalMs = 250): { stats: HubStats; lines: readonly string[] } {
  const [snap, setSnap] = useState(() => ({ stats: sensorHub.getStats(), lines: [...sensorHub.recentLines()] }));
  useEffect(() => {
    const id = setInterval(() => setSnap({ stats: sensorHub.getStats(), lines: [...sensorHub.recentLines()] }), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return snap;
}
