import { useEffect, type ReactNode } from 'react';
import { QUALITY_COPY } from '../settings/graphics';
import { useSettings, type GraphicsQuality } from '../settings/settingsStore';
import { useUi } from '../state/uiStore';
import { Button, Segmented, Switch } from '../ui/primitives';

function Row({ title, hint, children, disabled }: { title: string; hint?: string; children: ReactNode; disabled?: boolean }) {
  return (
    <div className={`flex items-start justify-between gap-6 py-2.5 ${disabled ? 'opacity-45' : ''}`}>
      <div>
        <div className="text-[13px] text-ink">{title}</div>
        {hint && <div className="mt-0.5 max-w-[340px] text-[12px] leading-snug text-ink-3">{hint}</div>}
      </div>
      <div className="shrink-0 pt-0.5">{children}</div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-t border-white/[0.06] pt-3 first:border-t-0 first:pt-0">
      <h3 className="label mb-1">{title}</h3>
      <div className="divide-y divide-white/[0.05]">{children}</div>
    </section>
  );
}

/** Settings, shared by the start menu and the pause menu. */
export function SettingsDialog() {
  const s = useSettings();
  const ui = useUi();
  const close = () => ui.setShowSettings(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        close();
      }
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  });

  return (
    <div className="anim-fade absolute inset-0 z-30 grid place-items-center bg-[rgb(4_7_12/0.55)] p-4 backdrop-blur-[4px]" onClick={close}>
      <div
        role="dialog"
        aria-label="Settings"
        className="glass-strong anim-scale max-h-full w-full max-w-[560px] overflow-y-auto rounded-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="sticky top-0 z-10 flex items-center justify-between border-b border-white/[0.06] bg-[rgb(9_14_24/0.9)] px-5 py-3 backdrop-blur">
          <h2 className="font-[family-name:var(--font-display)] text-[17px] font-semibold tracking-[0.2em]">SETTINGS</h2>
          <Button size="sm" variant="ghost" onClick={close}>
            Done
          </Button>
        </header>
        <div className="space-y-4 px-5 py-4">
          <Section title="Graphics">
            <div className="py-2.5">
              <Segmented<GraphicsQuality>
                label="Graphics quality"
                value={s.graphics}
                onChange={s.setGraphics}
                options={[
                  { value: 'low', label: 'Low' },
                  { value: 'medium', label: 'Medium' },
                  { value: 'high', label: 'High' },
                ]}
              />
              <p className="mt-2 text-[12px] leading-snug text-ink-3">{QUALITY_COPY[s.graphics]}</p>
            </div>
            <Row title="Bloom and vignette" hint="Soft glow on lights and the sun, darker corners. High quality only." disabled={s.graphics !== 'high'}>
              <Switch aria-label="Bloom and vignette" checked={s.postFx} disabled={s.graphics !== 'high'} onCheckedChange={s.setPostFx} />
            </Row>
            <Row title="Show frame rate" hint="A small FPS counter in the corner of the flight screen.">
              <Switch aria-label="Show frame rate" checked={s.showFps} onCheckedChange={s.setShowFps} />
            </Row>
          </Section>

          <Section title="Flying">
            <Row title="Easy flight" hint="Tilt sets bank and climb rate, speed is held, one key takes off, and the aircraft levels out when you let go.">
              <Switch aria-label="Easy flight" checked={s.easyFlight} onCheckedChange={s.setEasyFlight} />
            </Row>
            <Row title="Bank assist" hint="Roll input sets a bank angle instead of a roll rate. Always on in Easy flight." disabled={s.easyFlight}>
              <Switch aria-label="Bank assist" checked={s.easyFlight || s.bankAssist} disabled={s.easyFlight} onCheckedChange={s.setBankAssist} />
            </Row>
            <Row title="Auto-rudder" hint="Coordinates turns automatically. Always on in Easy flight." disabled={s.easyFlight}>
              <Switch aria-label="Auto-rudder" checked={s.easyFlight || s.autoRudder} disabled={s.easyFlight} onCheckedChange={s.setAutoRudder} />
            </Row>
          </Section>

          <Section title="Screen">
            <Row title="On-screen instruments" hint="The six-pack panel in the chase view (I).">
              <Switch aria-label="On-screen instruments" checked={ui.showInstruments} onCheckedChange={ui.toggleInstruments} />
            </Row>
            <Row title="Key card" hint="The controls reference in the corner (H).">
              <Switch aria-label="Key card" checked={ui.showHelp} onCheckedChange={ui.toggleHelp} />
            </Row>
          </Section>

          <Section title="Controller">
            <Row title="Motion controller" hint="Connection, calibration, sensitivity, dead zone and axis inversion.">
              <Button size="sm" onClick={() => ui.setScreen('setup')}>
                Open setup
              </Button>
            </Row>
          </Section>
        </div>
      </div>
    </div>
  );
}
