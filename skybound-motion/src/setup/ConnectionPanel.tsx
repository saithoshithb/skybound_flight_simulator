import { useEffect, useState } from 'react';
import { captureNeutral, ZERO_NEUTRAL } from '../calibration/neutral';
import { pipeline } from '../control/controlLoop';
import { cn } from '../lib/utils';
import { useHubStatus } from '../sensor/hooks';
import { sensorHub, type LinkState } from '../sensor/sensorHub';
import { canReconnect, connect, disconnect, reconnect } from '../sensor/serialSession';
import { serialSupport } from '../sensor/transport/webSerial';
import { useSettings } from '../settings/settingsStore';
import { Button, Panel, Segmented } from '../ui/primitives';

export const LINK_LABEL: Record<LinkState, { text: string; tone: 'ok' | 'warn' | 'bad' | 'idle' }> = {
  disconnected: { text: 'Not connected', tone: 'idle' },
  connecting: { text: 'Connecting…', tone: 'warn' },
  awaiting: { text: 'Waiting for sensor', tone: 'warn' },
  live: { text: 'Live', tone: 'ok' },
  stale: { text: 'Data late', tone: 'warn' },
  lost: { text: 'No data', tone: 'bad' },
};

const DEVICE_LABEL: Record<string, string> = {
  unknown: '—',
  calibrating: 'Calibrating gyro — hold still',
  ready: 'Ready',
  sensor_error: 'Sensor error',
  motion_during_cal: 'Moved during calibration, retrying',
  cal_failed: 'Calibration failed (using previous bias)',
};

export function StatusDot({ tone }: { tone: 'ok' | 'warn' | 'bad' | 'idle' }) {
  return (
    <span
      className={cn(
        'inline-block size-2 rounded-full',
        tone === 'ok' && 'bg-ok',
        tone === 'warn' && 'bg-warn',
        tone === 'bad' && 'bg-bad',
        tone === 'idle' && 'bg-ink-3',
      )}
    />
  );
}

type Busy = null | 'connect' | 'calibrate' | 'recenter';

export function ConnectionPanel() {
  const status = useHubStatus();
  const source = useSettings((s) => s.source);
  const setSource = useSettings((s) => s.setSource);
  const neutral = useSettings((s) => s.neutral);
  const setNeutral = useSettings((s) => s.setNeutral);
  const [busy, setBusy] = useState<Busy>(null);
  const [note, setNote] = useState<{ tone: 'ok' | 'bad'; text: string } | null>(null);
  const support = serialSupport();
  const connected = status.link !== 'disconnected' && status.link !== 'connecting';
  const usable = status.link === 'live' && status.device === 'ready';

  const run = async (kind: Busy, fn: () => Promise<void>) => {
    setBusy(kind);
    setNote(null);
    try {
      await fn();
    } catch (err) {
      setNote({ tone: 'bad', text: (err as Error).message });
    } finally {
      setBusy(null);
    }
  };

  const onRecenter = () =>
    run('recenter', async () => {
      const r = await captureNeutral(sensorHub);
      if (!r.ok) {
        setNote({ tone: 'bad', text: r.detail });
        return;
      }
      setNeutral(r.neutral);
      pipeline.reengage();
      setNote({ tone: 'ok', text: `Neutral set at ${r.neutral.pitch.toFixed(1)}° pitch, ${r.neutral.roll.toFixed(1)}° roll.` });
    });

  const onCalibrate = () =>
    run('calibrate', async () => {
      await sensorHub.send('CAL');
      setNote({ tone: 'ok', text: 'Gyro calibration started on the device. Keep the controller still for ~2 s.' });
    });

  // R = recenter, a hands-free-ish shortcut while holding the controller.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key.toLowerCase() === 'r' && !e.repeat && usable && !busy && !(e.target instanceof HTMLInputElement)) onRecenter();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  const link = LINK_LABEL[status.link];

  return (
    <Panel
      title="Connection"
      aside={
        <span className="flex items-center gap-2 text-[12px] font-semibold text-ink-2">
          <StatusDot tone={link.tone} />
          {link.text}
        </span>
      }
    >
      <div className="space-y-4">
        <div className="space-y-1.5">
          <div className="label">Source</div>
          <Segmented
            label="Sensor source"
            value={source}
            onChange={(v) => !connected && setSource(v)}
            options={[
              { value: 'serial', label: 'ESP32 · USB' },
              { value: 'simulated', label: 'Simulated' },
            ]}
          />
          {source === 'serial' && !support.ok && <p className="text-[12px] leading-snug text-bad">{support.reason}</p>}
        </div>

        <div className="grid grid-cols-2 gap-2">
          {!connected ? (
            <Button
              variant="primary"
              disabled={busy !== null || status.link === 'connecting' || (source === 'serial' && !support.ok)}
              onClick={() => run('connect', () => connect(source))}
            >
              {status.link === 'connecting' ? 'Connecting…' : 'Connect'}
            </Button>
          ) : (
            <Button variant="danger" onClick={() => run(null, disconnect)}>
              Disconnect
            </Button>
          )}
          {source === 'serial' && !connected && canReconnect() ? (
            <Button disabled={busy !== null} onClick={() => run('connect', reconnect)}>
              Reconnect
            </Button>
          ) : (
            <Button disabled={!connected || busy !== null || status.device === 'calibrating'} onClick={onCalibrate} title="Re-measure gyro bias on the device (hold still)">
              Calibrate gyro
            </Button>
          )}
          <Button className="col-span-2" disabled={!usable || busy !== null} onClick={onRecenter} title="Capture the current pose as neutral (shortcut: R)">
            {busy === 'recenter' ? 'Hold still…' : 'Recenter  ·  R'}
          </Button>
        </div>

        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-[12.5px]">
          <dt className="text-ink-3">Device</dt>
          <dd className={cn('text-right', status.device === 'sensor_error' && 'text-bad')}>
            {DEVICE_LABEL[status.device]}
            {status.deviceMessage && status.device === 'sensor_error' && <div className="text-[11.5px] text-ink-3">{status.deviceMessage}</div>}
          </dd>
          <dt className="text-ink-3">Port</dt>
          <dd className="truncate text-right">{status.transportLabel || '—'}</dd>
          <dt className="text-ink-3">Firmware</dt>
          <dd className="num text-right text-[12px]">{status.firmware ?? '—'}</dd>
          <dt className="text-ink-3">Neutral</dt>
          <dd className="num text-right text-[12px]">
            {neutral.capturedAt ? (
              <>
                {neutral.pitch.toFixed(1)}° / {neutral.roll.toFixed(1)}°{' '}
                <button className="ml-1 cursor-pointer text-ink-3 underline hover:text-ink-2" onClick={() => setNeutral(ZERO_NEUTRAL)}>
                  clear
                </button>
              </>
            ) : (
              <span className="text-warn">not set</span>
            )}
          </dd>
        </dl>

        {(note || status.lastError) && (
          <p className={cn('rounded-md border px-3 py-2 text-[12px] leading-snug', (note?.tone ?? 'bad') === 'ok' ? 'border-ok/30 text-ok' : 'border-bad/40 text-bad')}>
            {note?.text ?? status.lastError}
          </p>
        )}
      </div>
    </Panel>
  );
}
