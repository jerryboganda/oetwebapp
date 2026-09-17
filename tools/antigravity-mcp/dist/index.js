#!/usr/bin/env node
import { serveStdio } from "@modelcontextprotocol/server/stdio";
import { createAntigravityServer } from "./server.js";
import { assertNoByokPrimaryRoute, ensureUseG1CreditsFalse } from "./security.js";
ensureUseG1CreditsFalse();
try {
    assertNoByokPrimaryRoute();
}
catch (err) {
    console.error(err instanceof Error ? err.message : err);
    process.exit(1);
}
serveStdio(() => createAntigravityServer());
