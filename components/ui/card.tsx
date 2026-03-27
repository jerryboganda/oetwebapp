import { cn } from '@/lib/utils';
import { type HTMLAttributes, forwardRef } from 'react';

/* ─── Card Container ─── */
export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  hoverable?: boolean;
  padding?: 'none' | 'sm' | 'md' | 'lg';
}

const paddingStyles: Record<string, string> = {
  none: '',
  sm: 'p-4',
  md: 'p-5',
  lg: 'p-6',
};

export const Card = forwardRef<HTMLDivElement, CardProps>(
  ({ className, hoverable, padding = 'md', children, onClick, ...props }, ref) => (
    <div
      ref={ref}
      className={cn(
        'bg-surface border border-border rounded-2xl shadow-sm',
        paddingStyles[padding],
        hoverable && 'hover:shadow-clinical hover:border-border-hover transition-all duration-200 cursor-pointer',
        className,
      )}
      role={hoverable && onClick ? 'button' : undefined}
      tabIndex={hoverable && onClick ? 0 : undefined}
      onKeyDown={hoverable && onClick ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick(e as unknown as React.MouseEvent<HTMLDivElement>); } } : undefined}
      onClick={onClick}
      {...props}
    >
      {children}
    </div>
  ),
);
Card.displayName = 'Card';

/* ─── Card Header ─── */
export function CardHeader({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('mb-4', className)} {...props}>
      {children}
    </div>
  );
}

/* ─── Card Title ─── */
export function CardTitle({ className, children, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h3 className={cn('text-lg font-bold text-navy', className)} {...props}>
      {children}
    </h3>
  );
}

/* ─── Card Content ─── */
export function CardContent({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('', className)} {...props}>
      {children}
    </div>
  );
}

/* ─── Card Footer ─── */
export function CardFooter({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('mt-4 pt-4 border-t border-gray-100 flex items-center gap-3', className)} {...props}>
      {children}
    </div>
  );
}
