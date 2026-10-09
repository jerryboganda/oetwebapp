'use client';

import { useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import {
  runWritingValidatorSelfCheck,
  type WritingSelfCheckReport,
} from '@/lib/api/writing-validator-self-check';

/**
 * Writing hub panel: runs the clinical-abbreviation golden cases on the server against the REAL
 * validator and shows pass/fail per case. On-demand product evidence, never CI.
 */
export function WritingValidatorSelfCheck() {
  const [report, setReport] = useState<WritingSelfCheckReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run() {
    setBusy(true);
    setError(null);
    try {
      setReport(await runWritingValidatorSelfCheck());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Validator self-check: clinical abbreviations</CardTitle>
        <CardDescription>
          Runs the QD, QID, QDS, QOD, BD, TDS and PRN cases, scan and table forms, OD versus right eye, and completed versus
          pending actions through the live Model Answer validator on the server. Nothing is stored and no AI is called.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        <Button variant="outline" size="sm" disabled={busy} onClick={() => void run()}>
          <ShieldCheck className="h-4 w-4" aria-hidden="true" />
          {busy ? 'Running...' : 'Run self-check'}
        </Button>
        {error ? <p className="text-admin-fg-muted">Self-check failed to run: {error}</p> : null}
        {report ? (
          <div className="space-y-2">
            <p className="text-admin-fg-muted">
              Ran {new Date(report.ranAt).toLocaleString()} on validator {report.validatorVersion}:{' '}
              <strong>{report.passed}</strong> of {report.total} passed
              {report.failed > 0 ? `, ${report.failed} FAILED` : ''}.
            </p>
            <ul className="space-y-1">
              {report.cases.map((c) => (
                <li key={`${c.group}-${c.name}`} className="flex flex-wrap items-start gap-2">
                  {c.ok ? <Badge variant="success">Pass</Badge> : <Badge variant="danger">Fail</Badge>}
                  <span className="min-w-0 flex-1 text-admin-fg-default">
                    {c.group}: {c.name}
                    <span className="text-admin-fg-muted">
                      {' '}
                      (expected {c.expected ? 'a finding' : 'no finding'}, got {c.actual ? 'a finding' : 'none'}: {c.detail})
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}
