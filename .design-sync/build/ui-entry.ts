// design-sync entry: re-exports the components/ui primitives for the Claude
// Design bundle. Mirrors components/ui/index.ts EXCEPT CardLink, which wraps
// next/link — Next's client router can neither bundle cleanly into a standalone
// IIFE nor render outside a Next.js app, so it's omitted from the design system.
export { Button, type ButtonProps } from '../../components/ui/button';
export { Card, CardHeader, CardTitle, CardContent, CardFooter, type CardProps } from '../../components/ui/card';
export { Badge, StatusBadge, ScoreRangeBadge, ConfidenceBadge, CriterionChip, type BadgeProps, type StatusType, type ConfidenceLevel } from '../../components/ui/badge';
export { Tabs, TabPanel, type Tab } from '../../components/ui/tabs';
export { Accordion } from '../../components/ui/accordion';
export { Modal, Drawer } from '../../components/ui/modal';
export { InlineAlert, Toast } from '../../components/ui/alert';
export { FilterBar, type FilterGroup, type FilterOption } from '../../components/ui/filter-bar';
export { MobileFilterSheet } from '../../components/ui/mobile-filter-sheet';
export { MotionItem, MotionList, MotionPage, MotionSection } from '../../components/ui/motion-primitives';
export { ProgressBar, CircularProgress } from '../../components/ui/progress';
export { Timer } from '../../components/ui/timer';
export { Stepper, type Step } from '../../components/ui/stepper';
export { Skeleton, CardSkeleton, PageSkeleton } from '../../components/ui/skeleton';
export { EmptyState, ErrorState } from '../../components/ui/empty-error';
export { Input, Textarea, Select, Checkbox, RadioGroup, type InputProps, type TextareaProps, type SelectProps, type RadioOption } from '../../components/ui/form-controls';
export { DataTable, type Column } from '../../components/ui/data-table';
export { StatCard, type StatCardProps, type StatTrend } from '../../components/ui/stat-card';
