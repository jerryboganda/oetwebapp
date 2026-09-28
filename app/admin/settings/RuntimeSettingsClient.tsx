'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Save } from 'lucide-react';
import { Toast } from '@/components/ui/alert';
import { apiClient } from '@/lib/api';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { AdminSettingsLayout, SettingsNav } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import type { RuntimeSettingsResponse, RuntimeSettingsIntegrationTestResponse } from './runtime-settings-types';
import { type SectionId, type DataSectionId, type ToastState, type TestStatusState, EMAIL_FIELDS, BILLING_FIELDS, PAYPAL_FIELDS, SENTRY_FIELDS, BACKUP_FIELDS, OAUTH_FIELDS, PUSH_FIELDS, UPLOAD_SCANNER_FIELDS, SECURITY_FIELDS, VIDEO_PROTECTION_FIELDS, ZOOM_FIELDS, SPEAKING_WHISPER_FIELDS, SPEAKING_LIVEKIT_FIELDS, SPEAKING_AI_FIELDS, SPEAKING_STORAGE_FIELDS, SPEAKING_COMPLIANCE_FIELDS, SPEAKING_FEATURES_FIELDS, CHECKOUT_COM_FIELDS, BUNNY_STREAM_FIELDS, PAYMOB_FIELDS, EASYKASH_FIELDS, PAYTABS_FIELDS, SOKETI_FIELDS, DATA_RETENTION_FIELDS, EXPERT_AUTO_ASSIGNMENT_FIELDS, PASSWORD_POLICY_FIELDS, AI_ASSISTANT_FIELDS, AI_GATEWAY_FIELDS, WRITING_FIELDS, PLATFORM_FIELDS, MESSAGING_FIELDS, FX_FIELDS, BILLING_CORE_FIELDS, STORAGE_FIELDS, PDF_EXTRACTION_FIELDS, PRONUNCIATION_FIELDS, AUTH_TOKENS_FIELDS, WEB_PUSH_FIELDS, SUPPORT_FIELDS, FIREBASE_OTP_FIELDS } from './runtime-settings-fields';
import { SECTION_META } from './runtime-settings-defaults';
import { normalizeResponse, parseNullableNumberInput, getApiErrorMessage, formatTimestamp } from './runtime-settings-transforms';
import { SecretField, PlainField, DeviceExemptionEmailTable, Section, LoadingState, LockedState } from './runtime-settings-fields-ui';

export type { EmailSettings, BillingSettings, SentrySettings, BackupSettings, OAuthSettings, PushSettings, UploadScannerSettings, SecuritySettings, VideoProtectionSettings, ZoomSettings, SpeakingWhisperSettings, SpeakingLiveKitSettings, SpeakingAiSettings, SpeakingStorageSettings, SpeakingComplianceSettingsData, SpeakingFeaturesSettings, CheckoutComSettings, BunnyStreamSettings, PaymobSettings, EasyKashSettings, PayTabsSettings, SoketiSettings, DataRetentionSettings, ExpertAutoAssignmentSettings, PasswordPolicySettings, AiAssistantSettings, AiGatewaySettings, WritingSettings, PlatformSettings, MessagingSettings, FxSettings, BillingCoreSettings, StorageSettings, PdfExtractionSettings, PronunciationSettings, AuthTokensSettings, WebPushSettings, SupportSettings, FirebaseOtpSettings, RuntimeSettingsResponse, RuntimeSettingsIntegrationTestResponse } from './runtime-settings-types';

/* ───────────────────────── Main client ───────────────────────── */

export function RuntimeSettingsClient() {
  const { user, loading: authLoading } = useAuth();
  const isSystemAdmin = hasPermission(user?.adminPermissions ?? null, AdminPermission.SystemAdmin);

  const [server, setServer] = useState<RuntimeSettingsResponse | null>(null);
  const [draft, setDraft] = useState<RuntimeSettingsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testingSections, setTestingSections] = useState<Partial<Record<SectionId, boolean>>>({});
  const [testStatuses, setTestStatuses] = useState<TestStatusState>({});
  const [toast, setToast] = useState<ToastState>(null);
  const [openSections, setOpenSections] = useState<Record<SectionId, boolean>>({
    email: true,
    billing: false,
    paypal: false,
    sentry: false,
    backup: false,
    oauth: false,
    push: false,
    uploadScanner: false,
    zoom: false,
    speakingWhisper: false,
    speakingLiveKit: false,
    speakingAi: false,
    speakingStorage: false,
    speakingCompliance: false,
    speakingFeatures: false,
    checkoutCom: false,
    bunnyStream: false,
    paymob: false,
    payTabs: false,
    easyKash: false,
    soketi: false,
    dataRetention: false,
    expertAutoAssignment: false,
    passwordPolicy: false,
    aiAssistant: false,
    aiGateway: false,
    writing: false,
    platform: false,
    messaging: false,
    fx: false,
    billingCore: false,
    storage: false,
    pdfExtraction: false,
    pronunciation: false,
    authTokens: false,
    webPush: false,
    support: false,
    firebaseOtp: false,
    security: false,
    videoProtection: false,
  });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiClient.get<RuntimeSettingsResponse>('/v1/admin/runtime-settings');
      // Defensive merge so missing keys never blow up the UI.
      const merged = normalizeResponse(data);
      setServer(merged);
      setDraft(merged);
    } catch (err) {
      setToast({ variant: 'error', message: getApiErrorMessage(err, 'Failed to load runtime settings.') });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!isSystemAdmin) return;
    void load();
  }, [isSystemAdmin, load]);

  const toggleSection = useCallback((id: SectionId) => {
    setOpenSections((prev) => ({ ...prev, [id]: !prev[id] }));
  }, []);

  const updateField = useCallback(
    // Indexes the data payload, so the section param is a key of RuntimeSettingsResponse
    // (NOT a UI SectionId — the "paypal" UI section writes into the "billing" payload).
    <S extends DataSectionId, K extends keyof RuntimeSettingsResponse[S] & string>(
      section: S,
      key: K,
      value: RuntimeSettingsResponse[S][K],
    ) => {
      setDraft((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          [section]: {
            ...prev[section],
            [key]: value,
          },
        } as RuntimeSettingsResponse;
      });
    },
    [],
  );

  const handleSave = useCallback(async () => {
    if (!draft) return;
    setSaving(true);
    try {
      await apiClient.put('/v1/admin/runtime-settings', draft);
      await load();
      setToast({ variant: 'success', message: 'Runtime settings saved. Changes apply within ~30 seconds.' });
    } catch (err) {
      setToast({ variant: 'error', message: getApiErrorMessage(err, 'Failed to save runtime settings.') });
    } finally {
      setSaving(false);
    }
  }, [draft, load]);

  const handleTest = useCallback(async (section: SectionId) => {
    setTestingSections((prev) => ({ ...prev, [section]: true }));
    try {
      const result = await apiClient.post<RuntimeSettingsIntegrationTestResponse>(
        `/v1/admin/runtime-settings/test/${section}`,
      );
      setTestStatuses((prev) => ({ ...prev, [section]: result }));
      setToast({
        variant: result.status === 'ok' ? 'success' : 'error',
        message: result.message,
      });
    } catch (err) {
      const message = getApiErrorMessage(err, 'Failed to test integration.');
      setTestStatuses((prev) => ({
        ...prev,
        [section]: {
          section,
          status: 'failed',
          message,
          testedAt: new Date().toISOString(),
        },
      }));
      setToast({ variant: 'error', message });
    } finally {
      setTestingSections((prev) => ({ ...prev, [section]: false }));
    }
  }, []);

  const updatedLine = useMemo(() => {
    if (!server) return null;
    if (!server.updatedBy && !server.updatedAt) return null;
    return `Last updated by ${server.updatedBy ?? 'unknown'} at ${formatTimestamp(server.updatedAt)}`;
  }, [server]);

  if (authLoading) {
    return (
      <AdminSettingsLayout
        title="Runtime Settings"
        description="Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Runtime Settings' }]}
      >
        <LoadingState />
      </AdminSettingsLayout>
    );
  }

  if (!isSystemAdmin) {
    return (
      <AdminSettingsLayout
        title="Runtime Settings"
        description="Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Runtime Settings' }]}
      >
        <LockedState />
      </AdminSettingsLayout>
    );
  }

  const navItems = SECTION_META.map((s) => ({
    label: s.title,
    href: `#runtime-settings-${s.id}`,
  }));

  return (
    <AdminSettingsLayout
      title="Runtime Settings"
      description="Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds."
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Runtime Settings' }]}
      sidebar={<SettingsNav items={navItems} title="Sections" />}
      mobileNavLabel="Sections"
      actions={
        <Button
          variant="primary"
          onClick={handleSave}
          disabled={saving || !draft}
          loading={saving}
          loadingText="Saving…"
          startIcon={<Save className="h-4 w-4" aria-hidden="true" />}
          aria-label="Save all runtime settings"
        >
          Save All
        </Button>
      }
    >
      {loading || !draft || !server ? (
        <LoadingState />
      ) : (
        <div className="space-y-4">
          {SECTION_META.map((section) => (
            <Section
              key={section.id}
              id={section.id}
              title={section.title}
              description={section.description}
              open={openSections[section.id]}
              onToggle={() => toggleSection(section.id)}
              testing={Boolean(testingSections[section.id])}
              testStatus={testStatuses[section.id]}
              onTest={() => handleTest(section.id)}
            >
              {section.id === 'email' &&
                EMAIL_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.email[field.key] ?? '')}
                      draftValue={String(draft.email[field.key] ?? '')}
                      onChange={(next) => updateField('email', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.email[field.key] as string | number | null}
                      onChange={(next) =>
                        updateField(
                          'email',
                          field.key,
                          (field.type === 'number' && typeof next === 'string' ? parseNullableNumberInput(next) : next) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'billing' &&
                BILLING_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.billing[field.key] ?? '')}
                      draftValue={String(draft.billing[field.key] ?? '')}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.billing[field.key] as string}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'paypal' &&
                PAYPAL_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.billing[field.key] ?? '')}
                      draftValue={String(draft.billing[field.key] ?? '')}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.billing[field.key] as string | boolean | null}
                      onChange={(next) =>
                        updateField('billing', field.key, (field.type === 'checkbox' ? Boolean(next) : next) as never)
                      }
                    />
                  ),
                )}

              {/* Live/sandbox switch — lives with the PayPal credentials it applies to,
                  but persists into the billingCore payload object on save. */}
              {section.id === 'paypal' && (
                <>
                  <PlainField
                    label="Use PayPal Sandbox"
                    hint="UNCHECK this for live student payments. When ON, PayPal calls the sandbox (testing) API even if the credentials above are LIVE — real payments will 401. The credentials and this toggle MUST match (live keys → unchecked; sandbox keys → checked)."
                    type="checkbox"
                    value={draft.billingCore.paypalUseSandbox as boolean | null}
                    onChange={(next) => updateField('billingCore', 'paypalUseSandbox', Boolean(next) as never)}
                  />
                  <PlainField
                    label="PayPal API Base URL (advanced — leave blank)"
                    hint="Optional host override. Blank uses the host implied by the toggle above (live → https://api-m.paypal.com). Only set this for a custom/proxy endpoint."
                    type="url"
                    value={draft.billingCore.paypalApiBaseUrl as string | null}
                    onChange={(next) => updateField('billingCore', 'paypalApiBaseUrl', String(next) as never)}
                  />
                </>
              )}

              {section.id === 'sentry' &&
                SENTRY_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.sentry[field.key] ?? '')}
                      draftValue={String(draft.sentry[field.key] ?? '')}
                      onChange={(next) => updateField('sentry', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.sentry[field.key] as string | number | null}
                      onChange={(next) =>
                        updateField(
                          'sentry',
                          field.key,
                          (field.type === 'number' && typeof next === 'string' ? parseNullableNumberInput(next) : next) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'backup' &&
                BACKUP_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.backup[field.key] ?? '')}
                      draftValue={String(draft.backup[field.key] ?? '')}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.backup[field.key] as string}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'oauth' &&
                OAUTH_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.oauth[field.key] ?? '')}
                      draftValue={String(draft.oauth[field.key] ?? '')}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.oauth[field.key] as string}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'push' &&
                PUSH_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.push[field.key] ?? '')}
                      draftValue={String(draft.push[field.key] ?? '')}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.push[field.key] as string}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'uploadScanner' &&
                UPLOAD_SCANNER_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.uploadScanner[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'uploadScanner',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'zoom' &&
                ZOOM_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.zoom[field.key] ?? '')}
                      draftValue={String(draft.zoom[field.key] ?? '')}
                      onChange={(next) => updateField('zoom', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.zoom[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'zoom',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'speakingWhisper' &&
                SPEAKING_WHISPER_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingWhisper[field.key] ?? '')}
                      draftValue={String(draft.speakingWhisper[field.key] ?? '')}
                      onChange={(next) => updateField('speakingWhisper', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingWhisper[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'speakingWhisper',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'speakingLiveKit' &&
                SPEAKING_LIVEKIT_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingLiveKit[field.key] ?? '')}
                      draftValue={String(draft.speakingLiveKit[field.key] ?? '')}
                      onChange={(next) => updateField('speakingLiveKit', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingLiveKit[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'speakingLiveKit',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'speakingAi' &&
                SPEAKING_AI_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingAi[field.key] ?? '')}
                      draftValue={String(draft.speakingAi[field.key] ?? '')}
                      onChange={(next) => updateField('speakingAi', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingAi[field.key] as string | number | boolean | null}
                      onChange={(next) => updateField('speakingAi', field.key, String(next) as never)}
                    />
                  ),
                )}

              {section.id === 'speakingStorage' &&
                SPEAKING_STORAGE_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingStorage[field.key] ?? '')}
                      draftValue={String(draft.speakingStorage[field.key] ?? '')}
                      onChange={(next) => updateField('speakingStorage', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingStorage[field.key] as string | number | boolean | null}
                      onChange={(next) => updateField('speakingStorage', field.key, String(next) as never)}
                    />
                  ),
                )}

              {section.id === 'speakingCompliance' &&
                SPEAKING_COMPLIANCE_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.speakingCompliance[field.key] as string | number | null}
                    onChange={(next) =>
                      updateField(
                        'speakingCompliance',
                        field.key,
                        (field.type === 'number' && typeof next === 'string' ? parseNullableNumberInput(next) : next) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'speakingFeatures' &&
                SPEAKING_FEATURES_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.speakingFeatures[field.key] as boolean | null}
                    onChange={(next) => updateField('speakingFeatures', field.key, Boolean(next) as never)}
                  />
                ))}

              {section.id === 'checkoutCom' &&
                CHECKOUT_COM_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.checkoutCom[field.key] ?? '')}
                      draftValue={String(draft.checkoutCom[field.key] ?? '')}
                      onChange={(next) => updateField('checkoutCom', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.checkoutCom[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'checkoutCom',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'bunnyStream' &&
                BUNNY_STREAM_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.bunnyStream[field.key] ?? '')}
                      draftValue={String(draft.bunnyStream[field.key] ?? '')}
                      onChange={(next) => updateField('bunnyStream', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.bunnyStream[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'bunnyStream',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'bunnyStream' && (
                <p className="text-xs text-muted">
                  {server.bunnyStream.isConfigured
                    ? 'Bunny Stream credentials are configured.'
                    : 'Not fully configured yet — set the library id, API key, CDN hostname and token-auth key, then turn Enabled on.'}
                  {server.bunnyStream.videoAttestationKeyIds && server.bunnyStream.videoAttestationKeyIds.length > 0
                    ? ` App attestation keys set: ${server.bunnyStream.videoAttestationKeyIds.join(', ')}.`
                    : ' No app attestation keys set — playback stays disabled until they are added.'}
                </p>
              )}

              {section.id === 'paymob' &&
                PAYMOB_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.paymob[field.key] ?? '')}
                      draftValue={String(draft.paymob[field.key] ?? '')}
                      onChange={(next) => updateField('paymob', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.paymob[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'paymob',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'easyKash' &&
                EASYKASH_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.easyKash[field.key] ?? '')}
                      draftValue={String(draft.easyKash[field.key] ?? '')}
                      onChange={(next) => updateField('easyKash', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      options={field.options}
                      value={draft.easyKash[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'easyKash',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'payTabs' &&
                PAYTABS_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.payTabs[field.key] ?? '')}
                      draftValue={String(draft.payTabs[field.key] ?? '')}
                      onChange={(next) => updateField('payTabs', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.payTabs[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'payTabs',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'soketi' &&
                SOKETI_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.soketi[field.key] ?? '')}
                      draftValue={String(draft.soketi[field.key] ?? '')}
                      onChange={(next) => updateField('soketi', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.soketi[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'soketi',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'dataRetention' &&
                DATA_RETENTION_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.dataRetention[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'dataRetention',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'expertAutoAssignment' &&
                EXPERT_AUTO_ASSIGNMENT_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.expertAutoAssignment[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'expertAutoAssignment',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'passwordPolicy' &&
                PASSWORD_POLICY_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.passwordPolicy[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'passwordPolicy',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'aiAssistant' &&
                AI_ASSISTANT_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.aiAssistant[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'aiAssistant',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'aiGateway' &&
                AI_GATEWAY_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.aiGateway[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'aiGateway',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'writing' &&
                WRITING_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.writing[field.key] ?? '')}
                      draftValue={String(draft.writing[field.key] ?? '')}
                      onChange={(next) => updateField('writing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.writing[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'writing',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'platform' &&
                PLATFORM_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.platform[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'platform',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'messaging' &&
                MESSAGING_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.messaging[field.key] ?? '')}
                      draftValue={String(draft.messaging[field.key] ?? '')}
                      onChange={(next) => updateField('messaging', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.messaging[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'messaging',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'fx' &&
                FX_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.fx[field.key] ?? '')}
                      draftValue={String(draft.fx[field.key] ?? '')}
                      onChange={(next) => updateField('fx', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.fx[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'fx',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'billingCore' &&
                BILLING_CORE_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.billingCore[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'billingCore',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'storage' &&
                STORAGE_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.storage[field.key] ?? '')}
                      draftValue={String(draft.storage[field.key] ?? '')}
                      onChange={(next) => updateField('storage', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.storage[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'storage',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'pdfExtraction' &&
                PDF_EXTRACTION_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.pdfExtraction[field.key] ?? '')}
                      draftValue={String(draft.pdfExtraction[field.key] ?? '')}
                      onChange={(next) => updateField('pdfExtraction', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.pdfExtraction[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'pdfExtraction',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'pronunciation' &&
                PRONUNCIATION_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.pronunciation[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'pronunciation',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'authTokens' &&
                AUTH_TOKENS_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.authTokens[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'authTokens',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'webPush' &&
                WEB_PUSH_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.webPush[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'webPush',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'support' &&
                SUPPORT_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.support[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'support',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'firebaseOtp' &&
                FIREBASE_OTP_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.firebaseOtp[field.key] ?? '')}
                      draftValue={String(draft.firebaseOtp[field.key] ?? '')}
                      onChange={(next) => updateField('firebaseOtp', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.firebaseOtp[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'firebaseOtp',
                          field.key,
                          (field.type === 'checkbox' ? Boolean(next) : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'security' && (
                <DeviceExemptionEmailTable
                  value={draft.security.deviceVerificationExemptEmails}
                  onChange={(next) => updateField('security', 'deviceVerificationExemptEmails', next)}
                />
              )}

              {section.id === 'security' &&
                SECURITY_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    options={field.options}
                    value={draft.security[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'security',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'videoProtection' &&
                VIDEO_PROTECTION_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.videoProtection[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'videoProtection',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}
            </Section>
          ))}

          <div className="flex flex-col items-stretch gap-3 pt-2 sm:flex-row sm:items-center sm:justify-end">
            <Button
              variant="primary"
              onClick={handleSave}
              disabled={saving}
              loading={saving}
              loadingText="Saving…"
              startIcon={<Save className="h-4 w-4" aria-hidden="true" />}
              aria-label="Save all runtime settings"
            >
              Save All
            </Button>
          </div>

          {updatedLine && (
            <p className="text-right text-xs text-admin-fg-muted" data-testid="runtime-settings-updated-line">
              {updatedLine}
            </p>
          )}
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}

export default RuntimeSettingsClient;
