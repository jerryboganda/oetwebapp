import { cn } from '@/lib/utils';
import { type HTMLAttributes, forwardRef } from 'react';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  hoverable?: boolean;
  padding?: 'none' | 'sm' | 'md' | 'lg';
}

export type CardPadding = NonNullable<CardProps['padding']>;

// Mobile-dense, desktop-comfortable: tighter padding on phones (higher
// information density, native-app feel), scaling back up at `sm:` so desktop
// spacing is unchanged. The breakpoint lives in a variable so a caller's own
// `p-*` replaces the whole default (tailwind-merge drops `p-(--card-pad)`);
// a plain `sm:p-5` here used to survive the merge and override `p-6` above 640px.
const paddingStyles: Record<CardPadding, string> = {
  none: '',
  sm: 'p-(--card-pad) [--card-pad:0.625rem] sm:[--card-pad:1rem]',
  md: 'p-(--card-pad) [--card-pad:0.75rem] sm:[--card-pad:1.25rem]',
  lg: 'p-(--card-pad) [--card-pad:0.875rem] sm:[--card-pad:1.5rem]',
};

export function cardClassName({
  hoverable,
  interactive = false,
  padding = 'md',
}: {
  hoverable?: boolean;
  interactive?: boolean;
  padding?: CardPadding;
}) {
  return cn(
    'rounded-2xl border border-border bg-surface text-navy shadow-sm',
    paddingStyles[padding],
    hoverable && 'transition-[border-color,box-shadow,transform] duration-200 ease-standard',
    hoverable && 'hover:border-border-hover hover:shadow-clinical hoverable:-translate-y-0.5 active:translate-y-0 motion-reduce:transform-none',
    interactive && 'cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
  );
}

export const Card = forwardRef<HTMLDivElement, CardProps>(
  ({ className, hoverable, padding = 'md', children, ...props }, ref) => (
    <div
      ref={ref}
      className={cn(cardClassName({ hoverable, padding }), className)}
      {...props}
    >
      {children}
    </div>
  ),
);
Card.displayName = 'Card';

export function CardHeader({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('mb-4', className)} {...props}>
      {children}
    </div>
  );
}

export function CardTitle({ className, children, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h3 className={cn('text-lg font-bold text-navy', className)} {...props}>
      {children}
    </h3>
  );
}

export function CardDescription({ className, children, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return (
    <p className={cn('mt-1 text-sm text-muted', className)} {...props}>
      {children}
    </p>
  );
}

export function CardContent({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('', className)} {...props}>
      {children}
    </div>
  );
}

export function CardFooter({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('mt-4 flex items-center gap-3 border-t border-border pt-4', className)} {...props}>
      {children}
    </div>
  );
}
