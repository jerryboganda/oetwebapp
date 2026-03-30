'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import {
  IconBook2,
  IconBrandFacebook,
  IconBrandGoogle,
  IconBrandLinkedin,
  IconBriefcase,
  IconMail,
  IconMapPin,
} from '@tabler/icons-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import React, { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { buildExternalAuthStartHref, fetchSignupCatalog, registerLearner } from '@/lib/auth-client';
import CountryCodeSelect, {
  countryOptions,
} from '@/components/auth/country-code-select';
import AuthModeSwitch from '@/components/auth/auth-mode-switch';
import { PasswordField } from '@/components/auth/password-field';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import {
  enrollmentSessions as fallbackSessions,
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
import {
  signupPayloadSchema,
  type SignupPayloadFormValues,
} from '@/lib/auth/schemas';
import type {
  SignupExamType,
  SignupProfession,
  SignupSession,
} from '@/lib/types/auth';

const stepMeta = [
  { title: 'Personal', caption: 'Name, email, mobile' },
  { title: 'Enrollment', caption: 'Exam, profession, session' },
  { title: 'Security', caption: 'Password, consent, summary' },
] as const;

function ErrorText({ message }: { message: string | undefined }) {
  return message ? (
    <p className={styles.fieldHint} style={{ color: '#c23d69' }}>
      {message}
    </p>
  ) : null;
}

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
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
  const [examTypes, setExamTypes] = useState<SignupExamType[]>(fallbackExamTypes);
  const [professions, setProfessions] = useState<SignupProfession[]>(fallbackProfessions);
  const [enrollmentSessions, setEnrollmentSessions] = useState<SignupSession[]>(fallbackSessions);
  const nextPath = searchParams.get('next');
  const registrationToken = searchParams.get('registrationToken');
  const externalEmail = searchParams.get('email') ?? '';
  const externalFirstName = searchParams.get('firstName') ?? '';
  const externalLastName = searchParams.get('lastName') ?? '';

  const socials = useMemo(() => ([
    {
      href: buildExternalAuthStartHref('facebook', nextPath),
      label: 'Sign up with Facebook',
      icon: <IconBrandFacebook size={18} />,
    },
    {
      href: buildExternalAuthStartHref('google', nextPath),
      label: 'Sign up with Google',
      icon: <IconBrandGoogle size={18} />,
    },
    {
      href: buildExternalAuthStartHref('linkedin', nextPath),
      label: 'Sign up with LinkedIn',
      icon: <IconBrandLinkedin size={18} />,
    },
  ]), [nextPath]);

  const form = useForm<SignupPayloadFormValues>({
    resolver: zodResolver(signupPayloadSchema),
    mode: 'onTouched',
    defaultValues: {
      agreeToPrivacy: true,
      agreeToTerms: false,
      confirmPassword: '',
      countryTarget: '',
      email: externalEmail,
      examTypeId: '',
      firstName: externalFirstName,
      lastName: externalLastName,
      marketingOptIn: true,
      mobileNumber: '',
      password: '',
      professionId: '',
      sessionId: '',
    },
  });

  const selectedExamTypeId = form.watch('examTypeId');
  const selectedProfessionId = form.watch('professionId');
  const selectedSessionId = form.watch('sessionId');
  const selectedProfession = professions.find(
    (item) => item.id === selectedProfessionId
  );
  const selectedSession = enrollmentSessions.find(
    (item) => item.id === selectedSessionId
  );
  const filteredProfessions = useMemo(
    () =>
      professions.filter((item) =>
        selectedExamTypeId
          ? item.examTypeIds.includes(selectedExamTypeId)
          : true
      ),
    [professions, selectedExamTypeId]
  );
  const filteredSessions = useMemo(
    () =>
      enrollmentSessions.filter((item) => {
        if (selectedExamTypeId && item.examTypeId !== selectedExamTypeId) {
          return false;
        }
        if (
          selectedProfessionId &&
          !item.professionIds.includes(selectedProfessionId)
        ) {
          return false;
        }
        return true;
      }),
    [enrollmentSessions, selectedExamTypeId, selectedProfessionId]
  );
  const availableCountries = useMemo(() => {
    const source = selectedProfession?.countryTargets.length
      ? selectedProfession.countryTargets
      : filteredProfessions.flatMap((item) => item.countryTargets);

    return Array.from(new Set(source)).filter(Boolean);
  }, [filteredProfessions, selectedProfession]);

  useEffect(() => {
    let cancelled = false;

    const loadCatalog = async () => {
      try {
        const catalog = await fetchSignupCatalog();
        if (cancelled) {
          return;
        }

        setExamTypes(catalog.examTypes);
        setProfessions(catalog.professions);
        setEnrollmentSessions(catalog.sessions);
      } catch {
        if (!cancelled) {
          setExamTypes(fallbackExamTypes);
          setProfessions(fallbackProfessions);
          setEnrollmentSessions(fallbackSessions);
        }
      }
    };

    void loadCatalog();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const dialCode =
      countryOptions.find((item) => item.value === selectedCountryCode)
        ?.dialCode ?? '';

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
    if (
      selectedSessionId &&
      filteredSessions.some((item) => item.id === selectedSessionId)
    ) {
      return;
    }

    form.setValue('sessionId', filteredSessions[0]?.id ?? '', {
      shouldDirty: false,
      shouldValidate: false,
    });
  }, [filteredSessions, form, selectedSessionId]);

  useEffect(() => {
    const currentCountry = form.getValues('countryTarget');
    if (currentCountry && availableCountries.includes(currentCountry)) {
      return;
    }

    form.setValue('countryTarget', availableCountries[0] ?? '', {
      shouldDirty: false,
      shouldValidate: false,
    });
  }, [availableCountries, form]);

  const nextStep = async () => {
    const fields =
      step === 1
        ? (['firstName', 'lastName', 'email', 'mobileNumber'] as const)
        : ([
            'examTypeId',
            'professionId',
            'sessionId',
            'countryTarget',
          ] as const);

    const valid = await form.trigger(fields);
    if (valid) {
      setStep((current) => (current === 1 ? 2 : 3));
    }
  };

  const previousStep = () => {
    setStep((current) => (current === 3 ? 2 : 1));
  };

  const handleSubmit = form.handleSubmit(async (values) => {
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
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
          sessionId: values.sessionId,
          countryTarget: values.countryTarget,
          agreeToTerms: values.agreeToTerms,
          agreeToPrivacy: values.agreeToPrivacy,
          marketingOptIn: values.marketingOptIn,
          externalRegistrationToken: registrationToken,
        },
        { persistSession: false }
      );

      const selectedExam = examTypes.find(
        (item) => item.id === values.examTypeId
      );
      const selectedProfessionValue = professions.find(
        (item) => item.id === values.professionId
      );
      const selectedSessionValue = enrollmentSessions.find(
        (item) => item.id === values.sessionId
      );
      const params = new URLSearchParams({
        email: values.email,
        fullName: `${values.firstName} ${values.lastName}`.trim(),
        exam: selectedExam?.label ?? 'Exam',
        profession: selectedProfessionValue?.label ?? 'Profession',
        session: selectedSessionValue?.name ?? 'Session pending',
        sessionPrice: selectedSessionValue?.priceLabel ?? 'TBC',
        sessionMode: selectedSessionValue?.deliveryMode ?? 'online',
        sessionStart: selectedSessionValue?.startDate ?? 'TBC',
        country: values.countryTarget,
        stamp: new Intl.DateTimeFormat('en-GB', {
          day: '2-digit',
          month: 'short',
          year: 'numeric',
        }).format(new Date()) + ' at ' + new Intl.DateTimeFormat('en-US', {
          hour: 'numeric',
          minute: '2-digit',
          hour12: true,
        }).format(new Date()),
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
            const currentStep = index + 1;
            const isActive = step === currentStep;
            const isComplete = step > currentStep;

            return (
              <div
                key={item.title}
                className={`${styles.stepNode} ${
                  isActive ? styles.stepNodeActive : ''
                } ${isComplete ? styles.stepNodeComplete : ''}`}
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
                  {...form.register('firstName')}
                />
                <ErrorText message={errors.firstName?.message} />
              </div>
              <div className={styles.field}>
                <label htmlFor="lastName">Last Name</label>
                <input
                  id="lastName"
                  className={styles.input}
                  placeholder="Khan"
                  {...form.register('lastName')}
                />
                <ErrorText message={errors.lastName?.message} />
              </div>
            </div>

            <div className={styles.field}>
              <label htmlFor="email">Email Address</label>
              <input
                id="email"
                type="email"
                className={styles.input}
                placeholder="you@example.com"
                {...form.register('email')}
              />
              <ErrorText message={errors.email?.message} />
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
                  onChange={(event) =>
                    setMobileLocalNumber(event.target.value.replace(/\D/g, ''))
                  }
                />
              </div>
              <p className={styles.fieldHint}>
                Search by country, see the flag, and keep the dial code
                attached.
              </p>
              <ErrorText message={errors.mobileNumber?.message} />
            </div>
          </>
        ) : null}

        {step === 2 ? (
          <>
            <div className={styles.gridTwo}>
              <div className={styles.field}>
                <label htmlFor="examTypeId">Exam Type</label>
                <select
                  id="examTypeId"
                  className={styles.select}
                  {...form.register('examTypeId')}
                >
                  <option value="">Select exam type</option>
                  {examTypes.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.label}
                    </option>
                  ))}
                </select>
                <ErrorText message={errors.examTypeId?.message} />
              </div>
              <div className={styles.field}>
                <label htmlFor="professionId">Current Profession</label>
                <select
                  id="professionId"
                  className={styles.select}
                  {...form.register('professionId')}
                >
                  <option value="">Select profession</option>
                  {filteredProfessions.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.label}
                    </option>
                  ))}
                </select>
                <ErrorText message={errors.professionId?.message} />
              </div>
            </div>

            <div className={styles.field}>
              <label htmlFor="sessionId">Session</label>
              <select
                id="sessionId"
                className={styles.select}
                {...form.register('sessionId')}
              >
                <option value="">Select session</option>
                {filteredSessions.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name} - {item.priceLabel}
                  </option>
                ))}
              </select>
              <ErrorText message={errors.sessionId?.message} />
            </div>

            <div className={styles.field}>
              <label htmlFor="countryTarget">Target Country</label>
              <select
                id="countryTarget"
                className={styles.select}
                {...form.register('countryTarget')}
              >
                <option value="">Select target country</option>
                {availableCountries.map((item) => (
                  <option key={item} value={item}>
                    {item}
                  </option>
                ))}
              </select>
              <ErrorText message={errors.countryTarget?.message} />
            </div>

            <div className={styles.summaryCard}>
              <h4>Session Summary</h4>
              {selectedSession ? (
                <>
                  <p>{selectedSession.name}</p>
                  <p>
                    {selectedSession.priceLabel} -{' '}
                    {selectedSession.deliveryMode}
                  </p>
                  <p>
                    {selectedSession.startDate} to {selectedSession.endDate}
                  </p>
                  <p>
                    Seats left: {selectedSession.seatsRemaining}/
                    {selectedSession.capacity}
                  </p>
                </>
              ) : (
                <p>Select a session to preview the cohort summary.</p>
              )}
            </div>
          </>
        ) : null}

        {step === 3 ? (
          <>
            <PasswordField
              id="password"
              label="Password"
              placeholder="Create password"
              {...form.register('password')}
            />
            <ErrorText message={errors.password?.message} />

            <PasswordField
              id="confirmPassword"
              label="Confirm Password"
              placeholder="Repeat password"
              {...form.register('confirmPassword')}
            />
            <ErrorText message={errors.confirmPassword?.message} />

            <label className={styles.checkbox} htmlFor="agreeToTerms">
              <input
                id="agreeToTerms"
                type="checkbox"
                {...form.register('agreeToTerms')}
              />
              <span>I agree to the Terms and Conditions</span>
            </label>
            <ErrorText message={errors.agreeToTerms?.message} />

            <label className={styles.checkbox} htmlFor="agreeToPrivacy">
              <input
                id="agreeToPrivacy"
                type="checkbox"
                {...form.register('agreeToPrivacy')}
              />
              <span>I agree to the privacy policy and learner data notice</span>
            </label>
            <ErrorText message={errors.agreeToPrivacy?.message} />

            <label className={styles.checkbox} htmlFor="marketingOptIn">
              <input
                id="marketingOptIn"
                type="checkbox"
                {...form.register('marketingOptIn')}
              />
              <span>Send me session updates and preparation reminders</span>
            </label>

            <div className={styles.summaryCard}>
              <h4>Enrollment Summary</h4>
              <div className={styles.summaryList}>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconMail size={14} />
                  </span>
                  <p>
                    {form.getValues('firstName')} {form.getValues('lastName')} -{' '}
                    {form.getValues('email')}
                  </p>
                </div>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconBriefcase size={14} />
                  </span>
                  <p>
                    {examTypes.find((item) => item.id === selectedExamTypeId)
                      ?.label ?? 'Exam'}{' '}
                    - {selectedProfession?.label ?? 'Profession'}
                  </p>
                </div>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconBook2 size={14} />
                  </span>
                  <p>{selectedSession?.name ?? 'Session not selected yet'}</p>
                </div>
                <div className={styles.summaryItem}>
                  <span className={styles.summaryIcon}>
                    <IconMapPin size={14} />
                  </span>
                  <p>
                    {form.getValues('countryTarget') ||
                      'Target country not selected'}
                  </p>
                </div>
              </div>
            </div>
          </>
        ) : null}

        {errorMessage ? (
          <p className={`${styles.notice} ${styles.noticeDanger}`.trim()}>
            {errorMessage}
          </p>
        ) : null}

        <div className={styles.actionsRow}>
          {step > 1 ? (
            <button
              type="button"
              className={styles.secondaryButton}
              onClick={previousStep}
            >
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
            <button
              className={styles.submit}
              type="submit"
              disabled={isSubmitting}
            >
              {isSubmitting ? 'Creating Account...' : 'Create Account'}
            </button>
          )}
        </div>
      </form>
    </AuthScreenShell>
  );
}
