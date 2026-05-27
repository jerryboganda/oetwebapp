/** Pricing-page composables - Wave B2.
 *
 * These components were extracted from the inline `app/catalog/page.tsx`
 * implementation so the surface can be re-composed without copy/paste.
 * `/pricing` re-exports `/catalog`, so any change here affects both.
 */

export { Hero } from './Hero';
export type { PricingHeroProps, PricingHeroBadge } from './Hero';

export { BillingToggle } from './BillingToggle';
export type { BillingToggleProps, BillingCadence } from './BillingToggle';

export { CurrencyPicker, SUPPORTED_DISPLAY_CURRENCIES } from './CurrencyPicker';
export type { CurrencyPickerProps, DisplayCurrency } from './CurrencyPicker';

export { PackageCard, PriceCell } from './PackageCard';
export type { PackageCardProps } from './PackageCard';

export { FeatureMatrix } from './FeatureMatrix';
export type { FeatureMatrixProps } from './FeatureMatrix';

export { AddOnsGrid } from './AddOnsGrid';
export type { AddOnsGridProps } from './AddOnsGrid';

export { ClassPackages } from './ClassPackages';
export type { ClassPackagesProps } from './ClassPackages';

export { GuaranteeBanner } from './GuaranteeBanner';
export type { GuaranteeBannerProps } from './GuaranteeBanner';

export { Testimonials } from './Testimonials';
export type { TestimonialsProps, Testimonial } from './Testimonials';

export { FAQ } from './FAQ';
export type { FAQProps, FaqItem } from './FAQ';
