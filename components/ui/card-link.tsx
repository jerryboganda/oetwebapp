import Link, { type LinkProps } from 'next/link';
import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';
import { cardClassName, type CardPadding } from './card';

interface CardLinkProps extends LinkProps {
  children: ReactNode;
  className?: string;
  padding?: CardPadding;
}

export function CardLink({
  children,
  className,
  padding = 'md',
  ...props
}: CardLinkProps) {
  return (
    <Link
      className={cn(
        'block text-inherit no-underline',
        cardClassName({ hoverable: true, interactive: true, padding }),
        className,
      )}
      {...props}
    >
      {children}
    </Link>
  );
}
