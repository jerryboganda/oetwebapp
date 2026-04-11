-- Private Speaking Schema Migration
-- 20260411181309_AddPrivateSpeakingSchema

CREATE TABLE IF NOT EXISTS "PrivateSpeakingConfigs" (
    "Id" character varying(64) NOT NULL,
    "IsEnabled" boolean NOT NULL DEFAULT true,
    "DefaultSlotDurationMinutes" integer NOT NULL DEFAULT 30,
    "BufferMinutesBetweenSlots" integer NOT NULL DEFAULT 10,
    "MinBookingLeadTimeHours" integer NOT NULL DEFAULT 24,
    "MaxBookingAdvanceDays" integer NOT NULL DEFAULT 30,
    "ReservationTimeoutMinutes" integer NOT NULL DEFAULT 15,
    "DefaultPriceMinorUnits" integer NOT NULL DEFAULT 5000,
    "Currency" character varying(8) NOT NULL DEFAULT 'AUD',
    "CancellationWindowHours" integer NOT NULL DEFAULT 24,
    "AllowReschedule" boolean NOT NULL DEFAULT true,
    "RescheduleWindowHours" integer NOT NULL DEFAULT 24,
    "ReminderOffsetsHoursJson" character varying(256) NOT NULL DEFAULT '[24, 1]',
    "DailyReminderEnabled" boolean NOT NULL DEFAULT true,
    "DailyReminderHourUtc" integer NOT NULL DEFAULT 8,
    "CancellationPolicyText" character varying(2048),
    "BookingPolicyText" character varying(2048),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_PrivateSpeakingConfigs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "PrivateSpeakingTutorProfiles" (
    "Id" character varying(64) NOT NULL,
    "ExpertUserId" character varying(64) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "Bio" character varying(256),
    "Timezone" character varying(64) NOT NULL DEFAULT 'UTC',
    "PriceOverrideMinorUnits" integer,
    "SlotDurationOverrideMinutes" integer,
    "SpecialtiesJson" character varying(1024) NOT NULL DEFAULT '[]',
    "IsActive" boolean NOT NULL DEFAULT true,
    "TotalSessions" integer NOT NULL DEFAULT 0,
    "AverageRating" double precision NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_PrivateSpeakingTutorProfiles" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrivateSpeakingTutorProfiles_ExpertUserId" ON "PrivateSpeakingTutorProfiles" ("ExpertUserId");

CREATE TABLE IF NOT EXISTS "PrivateSpeakingAvailabilityRules" (
    "Id" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "DayOfWeek" integer NOT NULL,
    "StartTime" character varying(8) NOT NULL,
    "EndTime" character varying(8) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "EffectiveFrom" date,
    "EffectiveTo" date,
    CONSTRAINT "PK_PrivateSpeakingAvailabilityRules" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingAvailabilityRules_TutorProfiles" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingAvailabilityRules_TutorProfileId_DayOfWeek" ON "PrivateSpeakingAvailabilityRules" ("TutorProfileId", "DayOfWeek");

CREATE TABLE IF NOT EXISTS "PrivateSpeakingAvailabilityOverrides" (
    "Id" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "Date" date NOT NULL,
    "OverrideType" integer NOT NULL,
    "StartTime" character varying(8),
    "EndTime" character varying(8),
    "Reason" character varying(256),
    CONSTRAINT "PK_PrivateSpeakingAvailabilityOverrides" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingAvailabilityOverrides_TutorProfiles" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingAvailabilityOverrides_TutorProfileId_Date" ON "PrivateSpeakingAvailabilityOverrides" ("TutorProfileId", "Date");

CREATE TABLE IF NOT EXISTS "PrivateSpeakingBookings" (
    "Id" character varying(64) NOT NULL,
    "LearnerUserId" character varying(64) NOT NULL,
    "TutorProfileId" character varying(64) NOT NULL,
    "Status" integer NOT NULL DEFAULT 0,
    "SessionStartUtc" timestamp with time zone NOT NULL,
    "DurationMinutes" integer NOT NULL DEFAULT 30,
    "TutorTimezone" character varying(64) NOT NULL DEFAULT 'UTC',
    "LearnerTimezone" character varying(64) NOT NULL DEFAULT 'UTC',
    "PriceMinorUnits" integer NOT NULL DEFAULT 0,
    "Currency" character varying(8) NOT NULL DEFAULT 'AUD',
    "StripeCheckoutSessionId" character varying(256),
    "StripePaymentIntentId" character varying(256),
    "PaymentStatus" integer NOT NULL DEFAULT 0,
    "PaymentConfirmedAt" timestamp with time zone,
    "ZoomMeetingId" bigint,
    "ZoomJoinUrl" character varying(512),
    "ZoomStartUrl" character varying(512),
    "ZoomMeetingPassword" character varying(64),
    "ZoomStatus" integer NOT NULL DEFAULT 0,
    "ZoomError" character varying(512),
    "ZoomRetryCount" integer NOT NULL DEFAULT 0,
    "ReservationExpiresAt" timestamp with time zone,
    "IdempotencyKey" character varying(128),
    "LearnerNotes" character varying(2048),
    "TutorNotes" character varying(2048),
    "LearnerRating" integer,
    "LearnerFeedback" character varying(1024),
    "CancelledBy" character varying(64),
    "CancellationReason" character varying(512),
    "CancelledAt" timestamp with time zone,
    "RescheduledFromBookingId" character varying(64),
    "RemindersSentJson" character varying(256) NOT NULL DEFAULT '[]',
    "DailyReminderSent" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_PrivateSpeakingBookings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PrivateSpeakingBookings_TutorProfiles" FOREIGN KEY ("TutorProfileId") REFERENCES "PrivateSpeakingTutorProfiles" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingBookings_LearnerUserId_Status" ON "PrivateSpeakingBookings" ("LearnerUserId", "Status");
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingBookings_TutorProfileId_SessionStartUtc" ON "PrivateSpeakingBookings" ("TutorProfileId", "SessionStartUtc");
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingBookings_StripeCheckoutSessionId" ON "PrivateSpeakingBookings" ("StripeCheckoutSessionId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrivateSpeakingBookings_IdempotencyKey" ON "PrivateSpeakingBookings" ("IdempotencyKey");

CREATE TABLE IF NOT EXISTS "PrivateSpeakingAuditLogs" (
    "Id" character varying(64) NOT NULL,
    "BookingId" character varying(64),
    "ActorId" character varying(64) NOT NULL,
    "ActorRole" character varying(32) NOT NULL,
    "Action" character varying(64) NOT NULL,
    "Details" character varying(2048),
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_PrivateSpeakingAuditLogs" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingAuditLogs_BookingId" ON "PrivateSpeakingAuditLogs" ("BookingId");
CREATE INDEX IF NOT EXISTS "IX_PrivateSpeakingAuditLogs_ActorId_CreatedAt" ON "PrivateSpeakingAuditLogs" ("ActorId", "CreatedAt");

-- Record in migrations history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20260411181309_AddPrivateSpeakingSchema', '10.0.5') ON CONFLICT DO NOTHING;