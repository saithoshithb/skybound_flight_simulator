// shadcn/ui-style primitives (Radix + cva), themed for this app.
import * as SliderPrimitive from '@radix-ui/react-slider';
import { Slot } from '@radix-ui/react-slot';
import * as SwitchPrimitive from '@radix-ui/react-switch';
import { cva, type VariantProps } from 'class-variance-authority';
import type { ComponentProps, ReactNode } from 'react';
import { cn } from '../../lib/utils';

const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 rounded-md text-[13px] font-semibold tracking-[0.01em] transition-[background-color,border-color,color,box-shadow,transform] duration-150 active:scale-[0.98] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:pointer-events-none disabled:opacity-40 cursor-pointer select-none',
  {
    variants: {
      variant: {
        primary: 'bg-accent text-[#04121c] shadow-[0_0_0_1px_rgb(79_214_255/0.5),0_8px_24px_-10px_rgb(79_214_255/0.55)] hover:bg-[#7be2ff]',
        outline: 'border border-line bg-panel-2/70 text-ink hover:border-accent/50 hover:bg-panel-2',
        ghost: 'text-ink-2 hover:bg-white/[0.05] hover:text-ink',
        danger: 'border border-bad/50 text-bad hover:bg-bad/10',
      },
      size: { sm: 'h-8 px-3', md: 'h-9 px-4', lg: 'h-11 px-5 text-sm' },
    },
    defaultVariants: { variant: 'outline', size: 'md' },
  },
);

export function Button({
  className,
  variant,
  size,
  asChild,
  ...props
}: ComponentProps<'button'> & VariantProps<typeof buttonVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot : 'button';
  return <Comp className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}

export function Slider({ className, ...props }: ComponentProps<typeof SliderPrimitive.Root>) {
  return (
    <SliderPrimitive.Root className={cn('relative flex h-5 w-full touch-none items-center select-none', className)} {...props}>
      <SliderPrimitive.Track className="relative h-[3px] w-full grow overflow-hidden rounded-full bg-line">
        <SliderPrimitive.Range className="absolute h-full bg-accent/80" />
      </SliderPrimitive.Track>
      <SliderPrimitive.Thumb
        className="block size-3.5 cursor-grab rounded-full border-2 border-accent bg-ink shadow-[0_0_0_4px_rgb(79_214_255/0.12)] transition-[transform,box-shadow] duration-150 hover:scale-110 hover:shadow-[0_0_0_6px_rgb(79_214_255/0.16)] active:cursor-grabbing focus-visible:outline-2 focus-visible:outline-accent"
        aria-label={props['aria-label']}
      />
    </SliderPrimitive.Root>
  );
}

export function Switch({ className, ...props }: ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      className={cn(
        'inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border border-line bg-panel-2 transition-colors duration-200 data-[state=checked]:border-accent/70 data-[state=checked]:bg-accent/20 disabled:cursor-not-allowed focus-visible:outline-2 focus-visible:outline-accent',
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb className="block size-3.5 translate-x-0.5 rounded-full bg-ink-3 transition-[transform,background-color,box-shadow] duration-200 data-[state=checked]:translate-x-[18px] data-[state=checked]:bg-accent data-[state=checked]:shadow-[0_0_8px_rgb(79_214_255/0.6)]" />
    </SwitchPrimitive.Root>
  );
}

export function Segmented<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  value: T;
  options: { value: T; label: string }[];
  onChange: (v: T) => void;
  label: string;
}) {
  return (
    <div role="radiogroup" aria-label={label} className="flex rounded-md border border-line bg-bg/60 p-0.5">
      {options.map((o) => (
        <button
          key={o.value}
          role="radio"
          aria-checked={value === o.value}
          onClick={() => onChange(o.value)}
          className={cn(
            'h-7 flex-1 cursor-pointer rounded-[5px] px-2 text-[12px] font-semibold transition-colors duration-150',
            value === o.value ? 'bg-accent/15 text-accent shadow-[inset_0_0_0_1px_rgb(79_214_255/0.35)]' : 'text-ink-3 hover:bg-white/[0.04] hover:text-ink-2',
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

export function Panel({ title, aside, children, className }: { title: string; aside?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={cn('glass rounded-lg', className)}>
      <header className="flex items-center justify-between border-b border-white/[0.06] px-4 py-2.5">
        <h2 className="label">{title}</h2>
        {aside}
      </header>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function SliderRow({
  label,
  value,
  display,
  min,
  max,
  step,
  onChange,
  hint,
}: {
  label: string;
  value: number;
  display: string;
  min: number;
  max: number;
  step: number;
  onChange: (v: number) => void;
  hint?: string;
}) {
  return (
    <div className="space-y-1.5">
      <div className="flex items-baseline justify-between text-[12.5px]">
        <span className="text-ink-2" title={hint}>
          {label}
        </span>
        <span className="num text-ink">{display}</span>
      </div>
      <Slider aria-label={label} value={[value]} min={min} max={max} step={step} onValueChange={([v]) => onChange(v)} />
    </div>
  );
}

/**
 * Square icon button for toolbars. `active` marks a toggle that is on; the
 * label doubles as tooltip and accessible name.
 */
export function IconButton({
  label,
  active,
  shortcut,
  className,
  children,
  ...props
}: ComponentProps<'button'> & { label: string; active?: boolean; shortcut?: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-pressed={active}
      title={shortcut ? `${label} (${shortcut})` : label}
      className={cn(
        'glass relative grid size-9 cursor-pointer place-items-center rounded-md text-ink-2 transition-[color,border-color,background-color,transform] duration-150 hover:border-accent/40 hover:text-ink active:scale-95 focus-visible:outline-2 focus-visible:outline-accent',
        active && 'border-accent/50 text-accent',
        className,
      )}
      {...props}
    >
      {children}
      {active && <span className="absolute -bottom-px left-1/2 h-[2px] w-3 -translate-x-1/2 rounded-full bg-accent" />}
    </button>
  );
}
