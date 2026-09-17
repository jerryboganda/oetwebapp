'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import CountryCodeSelect from '@/components/auth/lazy-country-code-select';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { PasswordField } from '@/components/auth/password-field';
import { registerLearner } from '@/lib/auth-client';
import { captureAttribution, readAttribution } from '@/lib/attribution';
import { appendAuthNextParam, AUTH_ROUTES } from '@/lib/auth/routes';
import {
  placementSignupPayloadSchema,
  type PlacementSignupPayloadFormValues,
} from '@/lib/auth/schemas';
import { readErrorMessage } from '@/lib/read-error-message';
import { COUNTRY_OPTIONS } from '@/lib/countries';
import { toAsciiDigits } from '@/lib/normalize-digits';

/**
 * Minimal signup for the free General-English placement test
 * (/placement-test). Single screen: identity, mobile, password, consent.
 * Healthcare-enrollment fields are deliberately deferred — registration
 * sends `registrationPurpose: "placement"` and the learner supplies them
 * via the normal goals/onboarding flow when they enroll for OET.
 */
export function RegisterPlacementForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [selectedCountryCode, setSelectedCountryCode] = useState('pk');
  const [mobileLocalNumber, setMobileLocalNumber] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const nextPath = searchParams?.get('next') ?? null;
  const signInHref = appendAuthNextParam(AUTH_ROUTES.signIn, nextPath);

  const form = useForm<PlacementSignupPayloadFormValues>({
    resolver: zodResolver(placementSignupPayloadSchema),
    mode: 'onTouched',
    defaultValues: {
      agreeToPrivacy: false,
      agreeToTerms: false,
      confirmPassword: '',
      email: '',
      firstName: '',
      lastName: '',
      marketingOptIn: false,
      mobileNumber: '',
      password: '',
    },
  });

  useEffect(() => {
    const dialCode =
      COUNTRY_OPTIONS.find((item) => item.value === selectedCountryCode)?.dialCode ?? '';

    form.setValue('mobileNumber', `${dialCode}${mobileLocalNumber}`.trim(), {
      shouldDirty: true,
      shouldValidate: mobileLocalNumber.length > 0,
    });
  }, [form, mobileLocalNumber, selectedCountryCode]);

  useEffect(() => {
    captureAttribution();
  }, []);

  const handleSubmit = form.handleSubmit(async (values) => {
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const attribution = readAttribution();
      await registerLearner(
        {
          email: values.email.trim(),
          password: values.password,
          displayName: `${values.firstName} ${values.lastName}`.trim(),
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          mobileNumber: values.mobileNumber.trim(),
          agreeToTerms: values.agreeToTerms,
          agreeToPrivacy: values.agreeToPrivacy,
          marketingOptIn: values.marketingOptIn,
          registrationPurpose: 'placement',
          utmSource: attribution.utmSource,
          utmMedium: attribution.utmMedium,
          utmCampaign: attribution.utmCampaign,
          utmTerm: attribution.utmTerm,
          utmContent: attribution.utmContent,
          referrerUrl: attribution.referrer,
          landingPath: attribution.landingPath,
        },
        { persistSession: false },
      );

      // Straight back into the placement test — never the dashboard.
      router.push(appendAuthNextParam(AUTH_ROUTES.signUpSuccess, nextPath ?? '/placement-test'));
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Unable to create your account right now.'));
    } finally {
      setIsSubmitting(false);
    }
  });

  const errors = form.formState.errors;

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Free Placement Test"
      title="Create Your Free Account"
      footer={
        <>
          Already have an account?{' '}
          <Link className={styles.link} href={signInHref}>
            Sign in
          </Link>
        </>
      }
      terms={
        <Link className={styles.link} href={AUTH_ROUTES.terms}>
          Terms of use &amp; Conditions
        </Link>
      }
    >
      {/* method="post" defensive fallback — see sign-in-form.tsx for why. */}
      <form onSubmit={handleSubmit} method="post" className={styles.wizard}>
        <p className={styles.fieldHint}>
          One quick step, then your placement test starts. You can add your OET
          enrollment details later.
        </p>

        <div className={styles.gridTwo}>
          <div className={styles.field}>
            <label htmlFor="placement-firstName">First Name</label>
            <input
              id="placement-firstName"
              className={styles.input}
              placeholder="Aisha"
              autoComplete="given-name"
              {...form.register('firstName')}
            />
            <p className={styles.fieldHint}>{errors.firstName?.message}</p>
          </div>
          <div className={styles.field}>
            <label htmlFor="placement-lastName">Last Name</label>
            <input
              id="placement-lastName"
              className={styles.input}
              placeholder="Khan"
              autoComplete="family-name"
              {...form.register('lastName')}
            />
            <p className={styles.fieldHint}>{errors.lastName?.message}</p>
          </div>
        </div>

        <div className={styles.field}>
          <label htmlFor="placement-email">Email Address</label>
          <input
            id="placement-email"
            type="email"
            className={styles.input}
            placeholder="you@example.com"
            autoComplete="email"
            inputMode="email"
            spellCheck={false}
            {...form.register('email')}
          />
          <p className={styles.fieldHint}>{errors.email?.message}</p>
        </div>

        <div className={styles.field}>
          <label htmlFor="placement-mobile-local">Mobile Number</label>
          <div className={styles.inputGroup}>
            <CountryCodeSelect
              inputId="placement-mobile-country-code"
              value={selectedCountryCode}
              onChange={(option) => setSelectedCountryCode(option.value)}
            />
            <input
              id="placement-mobile-local"
              className={styles.input}
              placeholder="3001234567"
              value={mobileLocalNumber}
              autoComplete="tel-national"
              inputMode="numeric"
              onChange={(event) => setMobileLocalNumber(toAsciiDigits(event.target.value))}
            />
          </div>
          <p className={styles.fieldHint}>{errors.mobileNumber?.message}</p>
        </div>

        <PasswordField
          id="placement-password"
          label="Password"
          placeholder="Create password"
          autoComplete="new-password"
          {...form.register('password')}
        />
        <p className={styles.fieldHint}>{errors.password?.message}</p>

        <PasswordField
          id="placement-confirmPassword"
          label="Confirm Password"
          placeholder="Repeat password"
          autoComplete="new-password"
          {...form.register('confirmPassword')}
        />
        <p className={styles.fieldHint}>{errors.confirmPassword?.message}</p>

        <label className={styles.checkbox} htmlFor="placement-agreeToTerms">
          <input id="placement-agreeToTerms" type="checkbox" {...form.register('agreeToTerms')} />
          <span>I agree to the Terms and Conditions</span>
        </label>
        <p className={styles.fieldHint}>{errors.agreeToTerms?.message}</p>

        <label className={styles.checkbox} htmlFor="placement-agreeToPrivacy">
          <input id="placement-agreeToPrivacy" type="checkbox" {...form.register('agreeToPrivacy')} />
          <span>I agree to the privacy policy and learner data notice</span>
        </label>
        <p className={styles.fieldHint}>{errors.agreeToPrivacy?.message}</p>

        <button type="submit" className={styles.submit} disabled={isSubmitting}>
          {isSubmitting ? 'Creating your account…' : 'Create Account & Start Test'}
        </button>

        {errorMessage ? (
          <p role="alert" className={styles.fieldHint}>
            {errorMessage}
          </p>
        ) : null}
      </form>
    </AuthScreenShell>
  );
}
