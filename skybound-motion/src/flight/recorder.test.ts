import { describe, expect, it } from 'vitest';
import { clearFlightLog, flightLogCsv, recordFrame } from './recorder';

describe('flight recorder', () => {
  it('records at 5 Hz, keeps 60 s, and exports one CSV row per sample', () => {
    clearFlightLog();
    for (let i = 0; i < 60 * 70; i++) recordFrame(1 / 60); // 70 s of frames
    const lines = flightLogCsv().split('\n');
    expect(lines[0]).toMatch(/^# skybound flight log/);
    const header = lines[1].split(',');
    expect(header).toContain('cRoll');
    expect(header).toContain('tBank');
    const rows = lines.slice(2);
    expect(rows.length).toBeGreaterThanOrEqual(299);
    expect(rows.length).toBeLessThanOrEqual(300);
    for (const r of rows) expect(r.split(',').length).toBe(header.length);
  });
});
