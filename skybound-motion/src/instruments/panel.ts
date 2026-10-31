import type { Telemetry } from '../flight/dynamics';
import * as G from './gauges';

/**
 * The instrument panel as one canvas: the six-pack (airspeed, attitude,
 * altimeter / turn coordinator, heading, vertical speed) plus a tachometer
 * and warning lights. The same canvas is the cockpit panel texture and the
 * optional on-screen panel, so both always show identical values.
 */
export const PANEL_W = 1024;
export const PANEL_H = 512;
const R = 116;
const COL = [128, 384, 640];
const ROW = [128, 384];
const RIGHT_X = 896;

export interface PanelState {
  rpm: number;
  stall: boolean;
  brakes: boolean;
  easyFlight: boolean;
  lowAltitude: boolean;
}

export class InstrumentPanel {
  readonly canvas: HTMLCanvasElement;
  private readonly ctx: CanvasRenderingContext2D;
  private readonly faces: HTMLCanvasElement;
  /** Bumped on every redraw so texture users know to re-upload. */
  version = 0;

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = PANEL_W;
    this.canvas.height = PANEL_H;
    this.ctx = this.canvas.getContext('2d')!;
    this.faces = document.createElement('canvas');
    this.faces.width = PANEL_W;
    this.faces.height = PANEL_H;
    this.drawStatic(this.faces.getContext('2d')!);
  }

  private drawStatic(c: CanvasRenderingContext2D) {
    // Panel surface: matte, slightly textured dark grey.
    c.fillStyle = '#25282b';
    c.fillRect(0, 0, PANEL_W, PANEL_H);
    for (let i = 0; i < 4000; i++) {
      c.fillStyle = `rgba(255,255,255,${Math.random() * 0.025})`;
      c.fillRect(Math.random() * PANEL_W, Math.random() * PANEL_H, 2, 2);
    }
    G.drawAirspeedFace(c, COL[0], ROW[0], R);
    G.drawAltimeterFace(c, COL[2], ROW[0], R);
    G.drawTurnCoordinatorFace(c, COL[0], ROW[1], R);
    G.drawVsiFace(c, COL[2], ROW[1], R);
    G.drawTachFace(c, RIGHT_X, ROW[0], R * 0.92);
  }

  /** Redraw from telemetry. Cheap enough to run every frame. */
  draw(t: Telemetry, s: PanelState) {
    const c = this.ctx;
    c.drawImage(this.faces, 0, 0);
    G.drawAirspeedNeedle(c, COL[0], ROW[0], R, t.airspeedKt);
    G.drawAttitude(c, COL[1], ROW[0], R, t.pitchDeg, t.rollDeg);
    G.drawAltimeterNeedles(c, COL[2], ROW[0], R, t.altitudeFt);
    G.drawTurnCoordinatorDynamic(c, COL[0], ROW[1], R, t.turnRateDps, t.lateralG);
    G.drawHeading(c, COL[1], ROW[1], R, t.headingDeg);
    G.drawVsiNeedle(c, COL[2], ROW[1], R, t.verticalSpeedFpm);
    G.drawTachNeedle(c, RIGHT_X, ROW[0], R * 0.92, s.rpm);
    this.drawAnnunciators(c, t, s);
    this.version++;
  }

  private drawAnnunciators(c: CanvasRenderingContext2D, t: Telemetry, s: PanelState) {
    const lights: [string, boolean, string][] = [
      ['STALL', s.stall, '#e0483a'],
      ['LOW ALT', s.lowAltitude, '#e0a43a'],
      ['BRAKES', s.brakes, '#e0a43a'],
      ['EASY', s.easyFlight, '#5fb37a'],
    ];
    const x0 = RIGHT_X - 92;
    const y0 = ROW[1] - 92;
    lights.forEach(([name, on, color], i) => {
      const x = x0 + (i % 2) * 96;
      const y = y0 + Math.floor(i / 2) * 64;
      c.fillStyle = on ? color : '#1a1c1d';
      c.fillRect(x, y, 88, 48);
      c.strokeStyle = '#3a3d40';
      c.lineWidth = 3;
      c.strokeRect(x, y, 88, 48);
      c.fillStyle = on ? '#111' : '#55585b';
      c.font = '700 17px "Inter", "Segoe UI", sans-serif';
      c.textAlign = 'center';
      c.textBaseline = 'middle';
      c.fillText(name, x + 44, y + 25);
    });
    // Throttle position readout under the lights.
    c.fillStyle = '#9a9b97';
    c.font = '600 18px "Inter", "Segoe UI", sans-serif';
    c.fillText(`THR ${Math.round(t.throttle * 100)}%`, RIGHT_X, y0 + 152);
  }
}

/** Engine speed the tachometer shows: idle ~650 rpm, full power ~2,600. */
export function engineRpm(engine: number, airspeedKt: number) {
  return 650 + engine * 1850 + Math.min(150, airspeedKt * 1.2) * (0.3 + engine * 0.7);
}

let panel: InstrumentPanel | null = null;
/** App-wide panel, created on first use (needs a DOM). */
export function getPanel() {
  panel ??= new InstrumentPanel();
  return panel;
}
