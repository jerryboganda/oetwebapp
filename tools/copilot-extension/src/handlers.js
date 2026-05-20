import { config } from './config.js';

/**
 * Helper: call the OET backend copilot API.
 */
async function callBackend(method, path, body = null) {
  const url = `${config.API_URL}${path}`;
  const headers = {
    'Content-Type': 'application/json',
    'X-Copilot-Token': config.COPILOT_BACKEND_TOKEN,
  };

  const opts = { method, headers, signal: AbortSignal.timeout(config.BACKEND_TIMEOUT_MS) };
  if (body) opts.body = JSON.stringify(body);

  const res = await fetch(url, opts);
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`Backend ${method} ${path} returned ${res.status}: ${text}`);
  }
  return res.json();
}

/**
 * Search the OET codebase via the backend search endpoint.
 */
export async function handleCodeSearch(query) {
  try {
    const result = await callBackend('POST', '/v1/copilot/search', { query });
    const files = result.results || [];

    if (files.length === 0) {
      return `No results found for query: "${query}"`;
    }

    let response = `## Search Results for "${query}"\n\n`;
    for (const file of files.slice(0, 10)) {
      response += `### ${file.path}\n`;
      if (file.snippet) {
        response += `\`\`\`\n${file.snippet}\n\`\`\`\n`;
      }
      response += '\n';
    }
    return response;
  } catch (err) {
    return `❌ Search failed: ${err.message}`;
  }
}

/**
 * Explain a specific file in the codebase.
 */
export async function handleExplainCode(filePath) {
  try {
    const result = await callBackend('POST', '/v1/copilot/explain', { filePath });
    return result.explanation || `No explanation available for ${filePath}`;
  } catch (err) {
    return `❌ Failed to explain code: ${err.message}`;
  }
}

/**
 * Trigger a test run for the given scope.
 * @param {string} scope - e.g. "all", "backend", "frontend", or a specific test file
 */
export async function handleRunTests(scope) {
  try {
    const result = await callBackend('POST', '/v1/copilot/test', { scope });
    let response = `## Test Run: ${scope}\n\n`;
    response += `**Status:** ${result.status}\n`;
    response += `**Passed:** ${result.passed ?? '?'} | **Failed:** ${result.failed ?? '?'} | **Skipped:** ${result.skipped ?? '?'}\n\n`;
    if (result.output) {
      response += `### Output\n\`\`\`\n${result.output.slice(0, 2000)}\n\`\`\`\n`;
    }
    return response;
  } catch (err) {
    return `❌ Test run failed: ${err.message}`;
  }
}

/**
 * Get current deployment status.
 */
export async function handleDeployStatus() {
  try {
    const result = await callBackend('GET', '/v1/copilot/deploy/status');
    let response = `## Deployment Status\n\n`;
    response += `| Environment | Status | Version | Last Deployed |\n`;
    response += `|---|---|---|---|\n`;
    for (const env of result.environments || []) {
      response += `| ${env.name} | ${env.status} | ${env.version || '-'} | ${env.lastDeployed || '-'} |\n`;
    }
    return response;
  } catch (err) {
    return `❌ Failed to get deploy status: ${err.message}`;
  }
}

/**
 * Help scaffold a new component based on a description.
 */
export async function handleCreateComponent(description) {
  const templates = {
    page: `// app/(authenticated)/<name>/page.tsx
import React from 'react';

export default function NewPage() {
  return (
    <div className="container mx-auto p-6">
      <h1 className="text-2xl font-bold">{/* ${description} */}</h1>
    </div>
  );
}`,
    component: `// components/<name>.tsx
'use client';
import React from 'react';

interface Props {
  // Add props here
}

export function NewComponent({}: Props) {
  return (
    <div>
      {/* ${description} */}
    </div>
  );
}`,
    endpoint: `// backend/src/OetLearner.Api/Endpoints/<Name>Endpoints.cs
using Microsoft.AspNetCore.Mvc;

namespace OetLearner.Api.Endpoints;

public static class NewEndpoints
{
    public static void MapNewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/new")
            .RequireAuthorization()
            .WithTags("${description}");

        group.MapGet("/", async () => Results.Ok());
    }
}`,
  };

  let response = `## Component Scaffold\n\n`;
  response += `Based on: *"${description}"*\n\n`;
  response += `Here are templates you can use:\n\n`;

  response += `### React Page\n\`\`\`tsx\n${templates.page}\n\`\`\`\n\n`;
  response += `### React Component\n\`\`\`tsx\n${templates.component}\n\`\`\`\n\n`;
  response += `### Backend Endpoint\n\`\`\`csharp\n${templates.endpoint}\n\`\`\`\n\n`;
  response += `> Tip: Copy the template that best fits your need and customize it.\n`;

  return response;
}

/**
 * Determine intent from user message and route to appropriate handler.
 */
export function detectIntent(message) {
  const lower = message.toLowerCase().trim();

  if (lower.startsWith('/search') || lower.includes('find code') || lower.includes('search for')) {
    const query = message.replace(/^\/search\s*/i, '').trim() || message;
    return { handler: 'search', args: [query] };
  }

  if (lower.startsWith('/explain') || lower.includes('explain file') || lower.includes('what does')) {
    const filePath = message.replace(/^\/explain\s*/i, '').trim();
    return { handler: 'explain', args: [filePath] };
  }

  if (lower.startsWith('/test') || lower.includes('run test')) {
    const scope = message.replace(/^\/test\s*/i, '').trim() || 'all';
    return { handler: 'test', args: [scope] };
  }

  if (lower.startsWith('/deploy') || lower.includes('deploy status') || lower.includes('deployment')) {
    return { handler: 'deploy', args: [] };
  }

  if (lower.startsWith('/create') || lower.includes('scaffold') || lower.includes('create component')) {
    const desc = message.replace(/^\/create\s*/i, '').trim() || message;
    return { handler: 'create', args: [desc] };
  }

  // Default: treat as search
  return { handler: 'search', args: [message] };
}

/**
 * Route to the correct handler based on detected intent.
 */
export async function routeIntent(intent) {
  switch (intent.handler) {
    case 'search':
      return handleCodeSearch(...intent.args);
    case 'explain':
      return handleExplainCode(...intent.args);
    case 'test':
      return handleRunTests(...intent.args);
    case 'deploy':
      return handleDeployStatus(...intent.args);
    case 'create':
      return handleCreateComponent(...intent.args);
    default:
      return 'I didn\'t understand that command. Try `/search`, `/explain`, `/test`, `/deploy`, or `/create`.';
  }
}
