# AI calls via grounded gateway with one usage row

Every AI call routes through the grounded gateway helpers (`IAiGatewayService` / `IDirectAiCallRecorder`), which refuse ungrounded prompts; each physical provider call writes exactly one `AiUsageRecord` (success, error, or refusal) for billing and audit. Bypassing the gateway is untraceable spend.
