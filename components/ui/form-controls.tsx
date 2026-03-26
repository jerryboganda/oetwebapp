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
      <div className="flex flex-col gap-1">
        {label && <label htmlFor={inputId} className="text-sm font-semibold text-navy">{label}</label>}
        <input
          ref={ref}
          id={inputId}
          className={cn(
            'px-3 py-2 text-sm border rounded-lg bg-surface text-navy placeholder:text-muted/50 transition-all duration-200',
            'focus:outline-none focus:ring-4 focus:ring-primary/20 focus:border-primary',
            error ? 'border-red-400 focus:ring-red-400/20' : 'border-gray-300 hover:border-gray-400',
            className,
          )}
          aria-invalid={!!error}
          aria-describedby={error ? `${inputId}-error` : undefined}
          {...props}
        />
        {error && <p id={`${inputId}-error`} className="text-xs text-red-600">{error}</p>}
        {hint && !error && <p className="text-xs text-muted">{hint}</p>}
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
      <div className="flex flex-col gap-1">
        {label && <label htmlFor={inputId} className="text-sm font-semibold text-navy">{label}</label>}
        <textarea
          ref={ref}
          id={inputId}
          className={cn(
            'px-3 py-2 text-sm border rounded-lg bg-surface text-navy placeholder:text-muted/50 transition-all duration-200 resize-y min-h-[80px]',
            'focus:outline-none focus:ring-4 focus:ring-primary/20 focus:border-primary',
            error ? 'border-red-400 focus:ring-red-400/20' : 'border-gray-300 hover:border-gray-400',
            className,
          )}
          aria-invalid={!!error}
          aria-describedby={error ? `${inputId}-error` : undefined}
          {...props}
        />
        {error && <p id={`${inputId}-error`} className="text-xs text-red-600">{error}</p>}
        {hint && !error && <p className="text-xs text-muted">{hint}</p>}
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
      <div className="flex flex-col gap-1">
        {label && <label htmlFor={inputId} className="text-sm font-semibold text-navy">{label}</label>}
        <select
          ref={ref}
          id={inputId}
          className={cn(
            'px-3 py-2 text-sm border rounded-lg bg-surface text-navy transition-all duration-200 appearance-none',
            'focus:outline-none focus:ring-4 focus:ring-primary/20 focus:border-primary',
            error ? 'border-red-400 focus:ring-red-400/20' : 'border-gray-300 hover:border-gray-400',
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
        {hint && !error && <p className="text-xs text-muted">{hint}</p>}
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
    <label className={cn('flex items-start gap-2 cursor-pointer', className)}>
      <input
        ref={ref}
        type="checkbox"
        className="mt-0.5 w-4 h-4 rounded border-gray-300 text-primary focus:ring-primary"
        {...props}
      />
      <span className="text-sm text-navy">{label}</span>
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
    <fieldset className={cn('flex flex-col gap-1', className)}>
      {label && <legend className="text-sm font-semibold text-navy mb-2">{label}</legend>}
      <div className="flex flex-col gap-2">
        {options.map((opt) => (
          <label key={opt.value} className="flex items-start gap-2 cursor-pointer">
            <input
              type="radio"
              name={name}
              value={opt.value}
              checked={value === opt.value}
              onChange={() => onChange?.(opt.value)}
              className="mt-0.5 w-4 h-4 text-primary focus:ring-primary"
            />
            <div>
              <span className="text-sm font-medium text-navy">{opt.label}</span>
              {opt.description && <p className="text-xs text-muted">{opt.description}</p>}
            </div>
          </label>
        ))}
      </div>
      {error && <p className="text-xs text-red-600 mt-1">{error}</p>}
    </fieldset>
  );
}
