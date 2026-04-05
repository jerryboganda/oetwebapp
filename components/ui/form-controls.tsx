import { cn } from '@/lib/utils';
import { type InputHTMLAttributes, type SelectHTMLAttributes, type TextareaHTMLAttributes, forwardRef, type ReactNode } from 'react';

/* ─── Input ─── */
export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  hint?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, hint, className, id, ...props }, ref) => {
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-');
    return (
      <div className="flex flex-col gap-1.5">
        {label && <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-navy">{label}</label>}
        <input
          ref={ref}
          id={inputId}
          className={cn(
            'rounded-2xl border border-gray-200 bg-background-light px-4 py-3 text-sm text-navy shadow-sm transition-[border-color,box-shadow,color,background-color] duration-200',
            'focus:outline-none focus:ring-4 focus:ring-primary/15 focus:border-primary focus:bg-surface',
            error ? 'border-red-400 focus:ring-red-400/20' : 'hover:border-gray-300',
            className,
          )}
          aria-invalid={!!error}
          aria-describedby={error ? `${inputId}-error` : undefined}
          {...props}
        />
        {error && <p id={`${inputId}-error`} className="text-xs text-red-600">{error}</p>}
        {hint && !error && <p className="text-xs leading-5 text-muted">{hint}</p>}
      </div>
    );
  },
);
Input.displayName = 'Input';

/* ─── Textarea ─── */
export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  error?: string;
  hint?: string;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ label, error, hint, className, id, ...props }, ref) => {
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-');
    return (
      <div className="flex flex-col gap-1.5">
        {label && <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-navy">{label}</label>}
        <textarea
          ref={ref}
          id={inputId}
          className={cn(
            'min-h-[80px] resize-y rounded-2xl border border-gray-200 bg-background-light px-4 py-3 text-sm text-navy shadow-sm transition-[border-color,box-shadow,color,background-color] duration-200',
            'focus:outline-none focus:ring-4 focus:ring-primary/15 focus:border-primary focus:bg-surface',
            error ? 'border-red-400 focus:ring-red-400/20' : 'hover:border-gray-300',
            className,
          )}
          aria-invalid={!!error}
          aria-describedby={error ? `${inputId}-error` : undefined}
          {...props}
        />
        {error && <p id={`${inputId}-error`} className="text-xs text-red-600">{error}</p>}
        {hint && !error && <p className="text-xs leading-5 text-muted">{hint}</p>}
      </div>
    );
  },
);
Textarea.displayName = 'Textarea';

/* ─── Select ─── */
export interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  error?: string;
  hint?: string;
  options: { value: string; label: string }[];
  placeholder?: string;
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(
  ({ label, error, hint, options, placeholder, className, id, ...props }, ref) => {
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-');
    return (
      <div className="flex flex-col gap-1.5">
        {label && <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-navy">{label}</label>}
        <select
          ref={ref}
          id={inputId}
          className={cn(
            'appearance-none rounded-2xl border border-gray-200 bg-background-light px-4 py-3 text-sm text-navy shadow-sm transition-[border-color,box-shadow,color,background-color] duration-200',
            'focus:outline-none focus:ring-4 focus:ring-primary/15 focus:border-primary focus:bg-surface',
            error ? 'border-red-400 focus:ring-red-400/20' : 'hover:border-gray-300',
            className,
          )}
          aria-invalid={!!error}
          {...props}
        >
          {placeholder && <option value="">{placeholder}</option>}
          {options.map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
        {error && <p className="text-xs text-red-600">{error}</p>}
        {hint && !error && <p className="text-xs leading-5 text-muted">{hint}</p>}
      </div>
    );
  },
);
Select.displayName = 'Select';

/* ─── Checkbox ─── */
export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: string;
  error?: string;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(
  ({ label, error, className, ...props }, ref) => (
    <label className={cn('flex items-start gap-3 rounded-2xl border border-gray-200 bg-background-light px-4 py-3 shadow-sm transition-colors hover:border-gray-300', className)}>
      <input
        ref={ref}
        type="checkbox"
        className="mt-0.5 h-4 w-4 rounded border-gray-300 text-primary focus:ring-primary"
        {...props}
      />
      <span className="flex-1 text-sm text-navy">{label}</span>
      {error && <span className="text-xs text-red-600">{error}</span>}
    </label>
  ),
);
Checkbox.displayName = 'Checkbox';

/* ─── Radio Group ─── */
export interface RadioOption {
  value: string;
  label: string;
  description?: string;
}

interface RadioGroupProps {
  name: string;
  label?: string;
  options: RadioOption[];
  value?: string;
  onChange?: (value: string) => void;
  error?: string;
  className?: string;
}

export function RadioGroup({ name, label, options, value, onChange, error, className }: RadioGroupProps) {
  return (
    <fieldset className={cn('flex flex-col gap-2', className)}>
      {label && <legend className="mb-1 text-sm font-semibold tracking-tight text-navy">{label}</legend>}
      <div className="flex flex-col gap-2">
        {options.map((opt) => (
          <label key={opt.value} className="flex items-start gap-3 rounded-2xl border border-gray-200 bg-background-light px-4 py-3 shadow-sm transition-colors hover:border-gray-300">
            <input
              type="radio"
              name={name}
              value={opt.value}
              checked={value === opt.value}
              onChange={() => onChange?.(opt.value)}
              className="mt-0.5 h-4 w-4 text-primary focus:ring-primary"
            />
            <div>
              <span className="text-sm font-medium text-navy">{opt.label}</span>
              {opt.description && <p className="mt-1 text-xs leading-5 text-muted">{opt.description}</p>}
            </div>
          </label>
        ))}
      </div>
      {error && <p className="text-xs text-red-600 mt-1">{error}</p>}
    </fieldset>
  );
}
