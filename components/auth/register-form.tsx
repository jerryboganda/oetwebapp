'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import {
  IconBrandFacebook,
  IconBrandGoogle,
  IconBrandLinkedin,
} from '@tabler/icons-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { AnimatePresence, motion } from 'motion/react';
import { motionTokens } from '@/lib/motion';
import { MotionFadeSwitch } from '@/components/ui/motion-primitives';
import { buildExternalAuthStartHref, registerLearner } from '@/lib/auth-client';
import AuthModeSwitch from '@/components/auth/auth-mode-switch';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
import {
  signupPayloadSchema,
  type SignupPayloadFormValues,
} from '@/lib/auth/schemas';
import { RegisterEnrollmentStep } from '@/components/auth/register/register-enrollment-step';
import { RegisterPersonalStep } from '@/components/auth/register/register-personal-step';
import { RegisterSecurityStep } from '@/components/auth/register/register-security-step';
import { RegisterStepProgress } from '@/components/auth/register/register-step-progress';
import { countryOptions } from '@/components/auth/country-code-select';
import { useSignupCatalog } from '@/lib/hooks/use-signup-catalog';
import { TARGET_COUNTRY_OPTIONS } from '@/components/auth/register/target-countries';

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
  const prevStepRef = useRef(step);
  const stepDirection: 1 | -1 = step >= prevStepRef.current ? 1 : -1;
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [selectedCountryCode, setSelectedCountryCode] = useState('pk');
  const [mobileLocalNumber, setMobileLocalNumber] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const { examTypes, professions } = useSignupCatalog();
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
    const dialCode = countryOptions.find((item) => item.value === selectedCountryCode)?.dialCode ?? '';

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
    if (selectedProfessionId && filteredProfessions.some((item) => item.id === selectedProfessionId)) {
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

    // Force learners to make an explicit choice — the country dropdown is
    // mandatory and must not be silently auto-filled.
    form.setValue('countryTarget', '', {
      shouldDirty: false,
      shouldValidate: false,
    });
  }, [availableCountries, form]);

  const nextStep = async () => {
    const fields = step === 1
      ? (['firstName', 'lastName', 'email', 'mobileNumber'] as const)
      : (['examTypeId', 'professionId', 'countryTarget'] as const);

    const isValid = await form.trigger(fields);
    if (isValid) {
      prevStepRef.current = step;
      setStep((current) => (current === 1 ? 2 : 3));
    }
  };

  const previousStep = () => {
    prevStepRef.current = step;
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
          countryTarget: values.countryTarget,
          agreeToTerms: values.agreeToTerms,
          agreeToPrivacy: values.agreeToPrivacy,
          marketingOptIn: values.marketingOptIn,
          externalRegistrationToken: registrationToken,
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

        <RegisterStepProgress step={step} />

        <MotionFadeSwitch activeKey={`step-${step}`} direction={stepDirection}>
          {step === 1 ? (
            <RegisterPersonalStep
              form={form}
              mobileLocalNumber={mobileLocalNumber}
              onCountryCodeChange={setSelectedCountryCode}
              onMobileLocalNumberChange={setMobileLocalNumber}
              selectedCountryCode={selectedCountryCode}
            />
          ) : null}

          {step === 2 ? (
            <RegisterEnrollmentStep
              availableCountries={availableCountries}
              examTypes={examTypes}
              filteredProfessions={filteredProfessions}
              form={form}
            />
          ) : null}

          {step === 3 ? (
            <RegisterSecurityStep
              examTypes={examTypes}
              form={form}
              selectedExamTypeId={selectedExamTypeId}
              selectedProfession={selectedProfession}
            />
          ) : null}
        </MotionFadeSwitch>

        <AnimatePresence mode="wait">
          {errorMessage ? (
            <motion.p
              key="error"
              initial={{ opacity: 0, y: -6, height: 0 }}
              animate={{ opacity: 1, y: 0, height: 'auto' }}
              exit={{ opacity: 0, y: -6, height: 0 }}
              transition={{ duration: motionTokens.duration.fast, ease: motionTokens.ease.entrance }}
              className={`${styles.notice} ${styles.noticeDanger}`.trim()}
            >
              {errorMessage}
            </motion.p>
          ) : null}
        </AnimatePresence>

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
              {isSubmitting ? 'Creating Account…' : 'Create Account'}
            </button>
          )}
        </div>
      </form>
    </AuthScreenShell>
  );
}
