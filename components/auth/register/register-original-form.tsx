'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import {
  IconBrandFacebook,
  IconBrandGoogle,
  IconBrandLinkedin,
  IconBriefcase,
  IconMail,
  IconMapPin,
} from '@tabler/icons-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import AuthModeSwitch from '@/components/auth/auth-mode-switch';
import CountryCodeSelect, {
  countryOptions,
} from '@/components/auth/country-code-select';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { PasswordField } from '@/components/auth/password-field';
import { buildExternalAuthStartHref, registerLearner } from '@/lib/auth-client';
import { captureAttribution, readAttribution } from '@/lib/attribution';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
import {
  signupPayloadSchema,
  type SignupPayloadFormValues,
} from '@/lib/auth/schemas';
import { useSignupCatalog } from '@/lib/hooks/use-signup-catalog';
import { TARGET_COUNTRY_OPTIONS } from './target-countries';

const stepMeta = [
  { title: 'Personal', caption: 'Name, email, mobile' },
  { title: 'Enrollment', caption: 'Exam, profession, country' },
  { title: 'Security', caption: 'Password, consent, summary' },
] as const;

/**
 * Fixed target-country list per PRD Phase 2 §1. The canonical list now lives
 * in {@link ./target-countries.ts} and is shared with the legacy register-form.
 */

function readErrorMessage(error: unknown): string {
  if (
    error &&
    typeof error === 'object' &&
    'message' in error &&
    typeof error.message === 'string'
  ) {
    return error.message;
  }

  return 'Unable to create your account right now.';
}

export function RegisterForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks('signUp');
  const [step, setStep] = useState<1 | 2 | 3>(1);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [selectedCountryCode, setSelectedCountryCode] = useState('pk');
  const [mobileLocalNumber, setMobileLocalNumber] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const { examTypes, externalAuthProviders, professions } = useSignupCatalog();
  const nextPath = searchParams?.get('next') ?? null;
  const registrationToken = searchParams?.get('registrationToken') ?? null;
  const externalEmail = searchParams?.get('email') ?? '';
  const externalFirstName = searchParams?.get('firstName') ?? '';
  const externalLastName = searchParams?.get('lastName') ?? '';

  const socials = useMemo(
    () =>
      externalAuthProviders.map((provider) => ({
        href: buildExternalAuthStartHref(provider, nextPath),
        label:
          provider === 'facebook'
            ? 'Sign up with Facebook'
            : provider === 'google'
              ? 'Sign up with Google'
              : 'Sign up with LinkedIn',
        icon:
          provider === 'facebook' ? (
            <IconBrandFacebook size={18} />
          ) : provider === 'google' ? (
            <IconBrandGoogle size={18} />
          ) : (
            <IconBrandLinkedin size={18} />
          ),
      })),
    [externalAuthProviders, nextPath],
  );

  const form = useForm<SignupPayloadFormValues>({
    resolver: zodResolver(signupPayloadSchema),
    mode: 'onTouched',
    defaultValues: {
      agreeToPrivacy: false,
      agreeToTerms: false,
      confirmPassword: '',
      countryTarget: '',
      email: externalEmail,
      examTypeId: '',
      firstName: externalFirstName,
      lastName: externalLastName,
      marketingOptIn: false,
      mobileNumber: '',
      password: '',
      professionId: '',
    },
  });

  const selectedExamTypeId = form.watch('examTypeId');
  const selectedProfessionId = form.watch('professionId');
  const selectedProfession = professions.find((item) => item.id === selectedProfessionId);

  const filteredProfessions = useMemo(
    () =>
      professions.filter((item) =>
        selectedExamTypeId ? item.examTypeIds.includes(selectedExamTypeId) : true,
      ),
    [professions, selectedExamTypeId],
  );

  const availableCountries = useMemo<readonly string[]>(
    () => selectedProfession?.countryTargets?.length ? selectedProfession.countryTargets : TARGET_COUNTRY_OPTIONS,
    [selectedProfession],
  );

  useEffect(() => {
    const dialCode =
      countryOptions.find((item) => item.value === selectedCountryCode)?.dialCode ?? '';

    form.setValue('mobileNumber', `${dialCode}${mobileLocalNumber}`.trim(), {
      shouldDirty: true,
      shouldValidate: mobileLocalNumber.length > 0,
    });
  }, [form, mobileLocalNumber, selectedCountryCode]);

  useEffect(() => {
    if (!selectedExamTypeId && examTypes[0]?.id) {
      form.setValue('examTypeId', examTypes[0].id, {
        shouldDirty: false,
        shouldValidate: false,
      });
    }
  }, [examTypes, form, selectedExamTypeId]);

  useEffect(() => {
    if (
      selectedProfessionId &&
      filteredProfessions.some((item) => item.id === selectedProfessionId)
    ) {
      return;
    }

    form.setValue('professionId', filteredProfessions[0]?.id ?? '', {
      shouldDirty: false,
      shouldValidate: false,
    });
  }, [filteredProfessions, form, selectedProfessionId]);

  useEffect(() => {
    const currentCountry = form.getValues('countryTarget');
    if (currentCountry && availableCountries.includes(currentCountry)) {
      return;
    }

    // Force learners to make an explicit choice — the country dropdown is now
    // mandatory and must not be silently auto-filled.
    form.setValue('countryTarget', '', {
      shouldDirty: false,
      shouldValidate: false,
    });
  }, [availableCountries, form]);

  const nextStep = async () => {
    const fields =
      step === 1
        ? (['firstName', 'lastName', 'email', 'mobileNumber'] as const)
        : (['examTypeId', 'professionId', 'countryTarget'] as const);

    const isValid = await form.trigger(fields);
    if (isValid) {
      setStep((current) => (current === 1 ? 2 : 3));
    }
  };

  const previousStep = () => {
    setStep((current) => (current === 3 ? 2 : 1));
  };

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
          examTypeId: values.examTypeId,
          professionId: values.professionId,
          countryTarget: values.countryTarget,
          agreeToTerms: values.agreeToTerms,
          agreeToPrivacy: values.agreeToPrivacy,
          marketingOptIn: values.marketingOptIn,
          externalRegistrationToken: registrationToken,
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

      const selectedExam = examTypes.find((item) => item.id === values.examTypeId);
      const selectedProfessionValue = professions.find((item) => item.id === values.professionId);
      const params = new URLSearchParams({
        email: values.email,
        fullName: `${values.firstName} ${values.lastName}`.trim(),
        exam: selectedExam?.label ?? 'Exam',
        profession: selectedProfessionValue?.label ?? 'Profession',
        country: values.countryTarget,
        stamp: `${new Intl.DateTimeFormat('en-GB', {
          day: '2-digit',
          month: 'short',
          year: 'numeric',
        }).format(new Date())} at ${new Intl.DateTimeFormat('en-US', {
          hour: 'numeric',
          minute: '2-digit',
          hour12: true,
        }).format(new Date())}`,
      });

      if (nextPath) {
        params.set('next', nextPath);
      }

      router.push(`${AUTH_ROUTES.signUpSuccess}?${params.toString()}`);
    } catch (error) {
      setErrorMessage(readErrorMessage(error));
    } finally {
      setIsSubmitting(false);
    }
  });

  const errors = form.formState.errors;

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Create Workspace"
      title="Register Your Account"
      footer={
        <>
          Already have an account?{' '}
          <Link className={styles.link} href={flowLinks.primary}>
            Sign in
          </Link>
        </>
      }
      terms={
        <Link className={styles.link} href={AUTH_ROUTES.terms}>
          Terms of use &amp; Conditions
        </Link>
      }
      socials={socials}
    >
      <form onSubmit={handleSubmit} className={styles.wizard}>
        <AuthModeSwitch mode="signUp" />
        <input type="hidden" {...form.register('mobileNumber')} />

        <div className={styles.wizardProgress} aria-label="Signup progress">
          <div
            className={styles.wizardProgressBar}
            style={{
              transform: `scaleX(${(step - 1) / (stepMeta.length - 1)})`,
            }}
          />
          {stepMeta.map((item, index) => {
            const currentStep = (index + 1) as 1 | 2 | 3;
            const isActive = step === currentStep;
            const isComplete = step > currentStep;

            return (
              <div
                key={item.title}
                className={`${styles.stepNode} ${
                  isActive ? styles.stepNodeActive : ''
                } ${isComplete ? styles.stepNodeComplete : ''}`.trim()}
              >
                <span className={styles.stepDot}>{currentStep}</span>
                <div className={styles.stepText}>
                  <strong>{item.title}</strong>
                  <span>{item.caption}</span>
                </div>
              </div>
            );
          })}
        </div>

        {step === 1 ? (
          <>
            <div className={styles.gridTwo}>
              <div className={styles.field}>
                <label htmlFor="firstName">First Name</label>
                <input
                  id="firstName"
                  className={styles.input}
                  placeholder="Aisha"
                  autoComplete="given-name"
                  {...form.register('firstName')}
                />
                <p className={styles.fieldHint}>{errors.firstName?.message}</p>
              </div>
              <div className={styles.field}>
                <label htmlFor="lastName">Last Name</label>
                <input
                  id="lastName"
                  className={styles.input}
                  placeholder="Khan"
                  autoComplete="family-name"
                  {...form.register('lastName')}
                />
                <p className={styles.fieldHint}>{errors.lastName?.message}</p>
              </div>
            </div>

            <div className={styles.field}>
              <label htmlFor="email">Email Address</label>
              <input
                id="email"
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
              <label htmlFor="mobileNumberLocal">Mobile Number</label>
              <div className={styles.inputGroup}>
                <CountryCodeSelect
                  inputId="mobile-country-code"
                  value={selectedCountryCode}
                  onChange={(option) => setSelectedCountryCode(option.value)}
                />
                <input
                  id="mobileNumberLocal"
                  className={styles.input}
                  placeholder="3001234567"
                  value={mobileLocalNumber}
                  autoComplete="tel-national"
                  inputMode="numeric"
                  onChange={(event) =>
                    setMobileLocalNumber(event.target.value.replace(/\D/g, ''))
                  }
                />
              </div>
              <p className={styles.fieldHint}>
                Search by country, see the flag, and keep the dial code attached.
              </p>
              <p className={styles.fieldHint}>{errors.mobileNumber?.message}</p>
            </div>
          </>
        ) : null}

        {step === 2 ? (
          <>
            <div className={styles.gridTwo}>
              <div className={styles.field}>
                <label htmlFor="examTypeId">Exam Type</label>
                <select id="examTypeId" className={styles.select} {...form.register('examTypeId')}>
                  <option value="">Select exam type</option>
                  {examTypes.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.label}
                    </option>
                  ))}
                </select>
                <p className={styles.fieldHint}>{errors.examTypeId?.message}</p>
              </div>
              <div className={styles.field}>
                <label htmlFor="professionId">Current Profession</label>
                <select id="professionId" className={styles.select} {...form.register('professionId')}>
                  <option value="">Select profession</option>
                  {filteredProfessions.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.label}
                    </option>
                  ))}
                </select>
                <p className={styles.fieldHint}>{errors.professionId?.message}</p>
              </div>
            </div>

            <div className={styles.field}>
              <label htmlFor="countryTarget">Target Country</label>
              <select
                id="countryTarget"
                className={styles.select}
                required
                aria-required="true"
                {...form.register('countryTarget')}
              >
                <option value="">Select target country</option>
                {availableCountries.map((item) => (
                  <option key={item} value={item}>
                    {item}
                  </option>
                ))}
              </select>
              <p className={styles.fieldHint}>{errors.countryTarget?.message}</p>
            </div>
          </>
        ) : null}

        {step === 3 ? (
          <>
            <PasswordField
              id="password"
              label="Password"
              placeholder="Create password"
              autoComplete="new-password"
              {...form.register('password')}
            />
            <p className={styles.fieldHint}>{errors.password?.message}</p>

            <PasswordField
              id="confirmPassword"
              label="Confirm Password"
              placeholder="Repeat password"
              autoComplete="new-password"
              {...form.register('confirmPassword')}
            />
            <p className={styles.fieldHint}>{errors.confirmPassword?.message}</p>

            <label className={styles.checkbox} htmlFor="agreeToTerms">
              <input id="agreeToTerms" type="checkbox" {...form.register('agreeToTerms')} />
              <span>I agree to the Terms and Conditions</span>
            </label>
            <p className={styles.fieldHint}>{errors.agreeToTerms?.message}</p>

            <label className={styles.checkbox} htmlFor="agreeToPrivacy">
              <input id="agreeToPrivacy" type="checkbox" {...form.register('agreeToPrivacy')} />
              <span>I agree to the privacy policy and learner data notice</span>
            </label>
            <p className={styles.fieldHint}>{errors.agreeToPrivacy?.message}</p>

            <label className={styles.checkbox} htmlFor="marketingOptIn">
              <input id="marketingOptIn" type="checkbox" {...form.register('marketingOptIn')} />
              <span>Send me preparation reminders and platform updates</span>
            </label>

            <div className={styles.summaryCard}>
              <h4>Enrollment Summary</h4>
              <div className={styles.summaryList}>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconMail size={14} />
                  </span>
                  <p>
                    {form.getValues('firstName')} {form.getValues('lastName')} · {form.getValues('email')}
                  </p>
                </div>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconBriefcase size={14} />
                  </span>
                  <p>
                    {examTypes.find((item) => item.id === selectedExamTypeId)?.label ?? 'Exam'} ·{' '}
                    {selectedProfession?.label ?? 'Profession'}
                  </p>
                </div>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconMapPin size={14} />
                  </span>
                  <p>{form.getValues('countryTarget') || 'Target country not selected'}</p>
                </div>
              </div>
            </div>

            {errorMessage ? (
              <p className={`${styles.notice} ${styles.noticeDanger}`.trim()}>{errorMessage}</p>
            ) : null}
          </>
        ) : null}

        <div className={styles.actionsRow}>
          {step > 1 ? (
            <button type="button" className={styles.secondaryButton} onClick={previousStep}>
              Back
            </button>
          ) : (
            <Link href={flowLinks.primary} className={styles.secondaryButton}>
              Sign in instead
            </Link>
          )}

          {step < 3 ? (
            <button type="button" className={styles.submit} onClick={nextStep}>
              Next Step
            </button>
          ) : (
            <button className={styles.submit} type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Creating Account...' : 'Create Account'}
            </button>
          )}
        </div>
      </form>
    </AuthScreenShell>
  );
}
