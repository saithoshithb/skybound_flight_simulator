import { cn } from '../lib/utils';
import { useHubStats } from '../sensor/hooks';
import { Panel } from '../ui/primitives';

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'ok' | 'warn' | 'bad' }) {
  return (
    <div>
      <div className="label">{label}</div>
      <div className={cn('num mt-0.5 text-[15px]', tone === 'warn' && 'text-warn', tone === 'bad' && 'text-bad')}>{value}</div>
    </div>
  );
}

export function DiagnosticsPanel() {
  const { stats, lines } = useHubStats();
  const age = stats.lastSampleAgeMs;
  return (
    <Panel title="Link diagnostics">
      <div className="grid grid-cols-3 gap-x-4 gap-y-3 sm:grid-cols-6">
        <Stat label="Rate" value={`${stats.rxHz} Hz`} tone={stats.samples && stats.rxHz < 30 ? 'warn' : undefined} />
        <Stat label="Last sample" value={Number.isFinite(age) ? `${Math.round(age)} ms` : '—'} tone={age > 2000 && Number.isFinite(age) ? 'bad' : age > 250 && Number.isFinite(age) ? 'warn' : undefined} />
        <Stat label="Samples" value={stats.samples.toLocaleString()} />
        <Stat label="Dropped" value={stats.droppedSeq.toLocaleString()} tone={stats.droppedSeq ? 'warn' : undefined} />
        <Stat label="Malformed" value={stats.malformed.toLocaleString()} tone={stats.malformed ? 'warn' : undefined} />
        <Stat label="Restarts" value={String(stats.restarts)} tone={stats.restarts ? 'warn' : undefined} />
      </div>
      {stats.lastMalformedReason && <p className="mt-3 text-[11.5px] text-ink-3">Last rejected line: {stats.lastMalformedReason}</p>}
      <pre className="num mt-3 h-[112px] overflow-hidden rounded-md border border-line bg-bg px-3 py-2 text-[10.5px] leading-[1.45] text-ink-3">
        {lines.length ? lines.slice(-7).join('\n') : 'Raw serial lines appear here.'}
      </pre>
    </Panel>
  );
}
