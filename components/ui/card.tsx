import { cn } from '@/lib/utils';
import { type HTMLAttributes, forwardRef } from 'react';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  hoverable?: boolean;
  padding?: 'none' | 'sm' | 'md' | 'lg';
}

export type CardPadding = NonNullable<CardProps['padding']>;

const paddingStyles: Record<CardPadding, string> = {
  none: '',
  sm: 'p-4',
  md: 'p-5',
  lg: 'p-6',
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
    'rounded-2xl border border-border bg-surface shadow-sm',
    paddingStyles[padding],
    hoverable && 'transition-[border-color,box-shadow,transform] duration-200',
    hoverable && 'hover:border-border-hover hover:shadow-clinical',
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

export function CardContent({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('', className)} {...props}>
      {children}
    </div>
  );
}

export function CardFooter({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('mt-4 flex items-center gap-3 border-t border-gray-100 pt-4', className)} {...props}>
      {children}
    </div>
  );
}
