/**
 * OET Statement of Results — pixel-faithful re-creation of the official
 * CBLA Statement of Results. Design contract: `docs/OET-RESULT-CARD-SPEC.md`.
 *
 * MISSION CRITICAL: this visual must match the official SoR side-by-side
 * at 100% zoom. Do not restyle, do not reinterpret, do not "improve".
 * Any change requires updating the spec doc first and pixel-diffing against
 * the reference screenshots in
 * `Project Real Content/Create Similar Table Formats for Results to show
 *  to Candidates/`.
 *
 * Component is pure — all data flows in via props. No data fetching here.
 */

'use client';

import { type CSSProperties, type ReactElement } from 'react';
import { oetGradeFromScaled, OET_SCALED_MAX, OET_SCALED_MIN } from '@/lib/scoring';

// ─────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────

export interface OetSorCandidate {
  name: string;
  candidateNumber: string;
  dateOfBirth?: string;
  gender?: 'Male' | 'Female' | 'Non-binary' | 'Prefer not to say';
}

export interface OetSorVenue {
  name: string;
  number: string;
  country: string;
}

export interface OetSorTest {
  date: string;
  deliveryMode: string;
  profession: string;
}

export interface OetSorScores {
  listening: number;
  reading: number;
  speaking: number;
  writing: number;
}

export interface OetStatementOfResults {
  candidate: OetSorCandidate;
  venue: OetSorVenue;
  test: OetSorTest;
  scores: OetSorScores;
  isPractice: boolean;
  issuedAt: string;
}

export interface OetStatementOfResultsProps {
  data: OetStatementOfResults;
  className?: string;
  /** When true, omit the practice disclaimer. Only for internal QA comparisons. */
  suppressPracticeDisclaimer?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────
// Chart geometry — extracted constants so tests can assert math directly
// ─────────────────────────────────────────────────────────────────────────

export const CHART = {
  width: 720,
  height: 420,
  padTop: 16,
  padRight: 24,
  padBottom: 40,
  padLeft: 60, // y-axis labels gutter
  bandColumnWidth: 28,
  subtestGap: 2,
  barHeight: 18,
  ticks: [0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500] as const,
  thresholds: [100, 200, 300, 350, 450] as const,
} as const;

type Band = { grade: 'A' | 'B' | 'C+' | 'C' | 'D' | 'E'; min: number; max: number; fill: string; labelFill: string };

export const BANDS: readonly Band[] = [
  { grade: 'A', min: 450, max: 500, fill: '#2B6F9F', labelFill: '#ffffff' },
  { grade: 'B', min: 350, max: 450, fill: '#5B9AC4', labelFill: '#ffffff' },
  { grade: 'C+', min: 300, max: 350, fill: '#9DC0DB', labelFill: '#333333' },
  { grade: 'C', min: 200, max: 300, fill: '#BDD6E8', labelFill: '#333333' },
  { grade: 'D', min: 100, max: 200, fill: '#D6E3ED', labelFill: '#333333' },
  { grade: 'E', min: 0, max: 100, fill: '#EBF0F4', labelFill: '#333333' },
] as const;

const SUBTESTS = ['listening', 'reading', 'speaking', 'writing'] as const;
type Subtest = (typeof SUBTESTS)[number];

const SUBTEST_LABELS: Record<Subtest, string> = {
  listening: 'Listening',
  reading: 'Reading',
  speaking: 'Speaking',
  writing: 'Writing',
};

// ─────────────────────────────────────────────────────────────────────────
// Math helpers — unit-testable
// ─────────────────────────────────────────────────────────────────────────

/** Chart plot rectangle (inside padding). */
function plotRect() {
  return {
    x: CHART.padLeft,
    y: CHART.padTop,
    w: CHART.width - CHART.padLeft - CHART.padRight,
    h: CHART.height - CHART.padTop - CHART.padBottom,
  };
}

/** Map a 0–500 score to a y-coordinate inside the plot rect. 500 at top. */
export function scoreToY(score: number): number {
  const { y, h } = plotRect();
  const clamped = Math.min(OET_SCALED_MAX, Math.max(OET_SCALED_MIN, score));
  return y + h * (1 - clamped / OET_SCALED_MAX);
}

/** Column geometry for the four subtest columns. */
export function subtestColumnX(index: number): { x: number; w: number } {
  const { x, w } = plotRect();
  const bandW = CHART.bandColumnWidth;
  const gap = CHART.subtestGap;
  const colsStart = x + bandW + gap;
  const colsW = w - bandW - gap;
  const oneCol = (colsW - gap * 3) / 4;
  return { x: colsStart + index * (oneCol + gap), w: oneCol };
}

/** Band column rectangle. */
function bandColumnRect() {
  const { x, y, h } = plotRect();
  return { x, y, w: CHART.bandColumnWidth, h };
}

/** Whether the score label fits inside the bar, or needs to sit outside. */
export function labelFitsInside(score: number): boolean {
  const y = scoreToY(score);
  const { y: top, h } = plotRect();
  const halfBar = CHART.barHeight / 2;
  return y - halfBar > top + 2 && y + halfBar < top + h - 2;
}

// ─────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────

export function OetStatementOfResultsCard({
  data,
  className,
  suppressPracticeDisclaimer = false,
}: OetStatementOfResultsProps): ReactElement {
  return (
    <article
      className={`oet-sor ${className ?? ''}`}
      aria-labelledby="oet-sor-title"
      style={rootStyle}
    >
      <h2 id="oet-sor-title" className="sr-only">
        OET Statement of Results for {data.candidate.name}
      </h2>

      <TestDetailsTable data={data} />
      <TestResultsStrip scores={data.scores} />
      <BandChart scores={data.scores} />
      <AccessibleTable data={data} />
      <CertificationBlock />
      <Footers isPractice={data.isPractice} suppress={suppressPracticeDisclaimer} />
    </article>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// Test Details table (§2 of the spec)
// ─────────────────────────────────────────────────────────────────────────

function TestDetailsTable({ data }: { data: OetStatementOfResults }) {
  const rows: Array<[string, string | undefined]> = [
    ['Candidate Name', data.candidate.name],
    ['Candidate Number', data.candidate.candidateNumber],
    ['Date of Birth', data.candidate.dateOfBirth],
    ['Gender', data.candidate.gender],
    ['Venue Name', data.venue.name],
    ['Venue Number', data.venue.number],
    ['Venue Country', data.venue.country],
    ['Test date', data.test.date],
    ['Test Delivery Mode', data.test.deliveryMode],
    ['Profession', data.test.profession],
  ];

  return (
    <div style={{ width: '100%' }} aria-hidden="true">
      <div style={sectionHeaderGrey}>TEST DETAILS:</div>
      <table style={detailsTable} role="presentation">
        <tbody>
          {rows.filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => (
            <tr key={k} style={{ borderBottom: '1px solid #D0D0D0' }}>
              <th scope="row" style={detailsKey}>{k}</th>
              <td style={detailsVal}>{v}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// TEST RESULTS summary strip (§3)
// ─────────────────────────────────────────────────────────────────────────

function TestResultsStrip({ scores }: { scores: OetSorScores }) {
  return (
    <div style={{ width: '100%' }} aria-hidden="true">
      <div style={sectionHeaderBlue}>TEST RESULTS</div>
      <div style={stripRow}>
        {SUBTESTS.map((s, i) => (
          <div key={`h-${s}`} style={{
            ...stripHeaderCell,
            borderRight: i === SUBTESTS.length - 1 ? 'none' : '1px solid rgba(255,255,255,0.4)',
          }}>
            {SUBTEST_LABELS[s]}:
          </div>
        ))}
      </div>
      <div style={stripRow}>
        {SUBTESTS.map((s, i) => (
          <div key={`v-${s}`} style={{
            ...stripValueCell,
            borderRight: i === SUBTESTS.length - 1 ? 'none' : '1px solid rgba(255,255,255,0.4)',
          }}>
            {scores[s]}
          </div>
        ))}
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// Band chart SVG (§4) — the signature visual
// ─────────────────────────────────────────────────────────────────────────

function BandChart({ scores }: { scores: OetSorScores }) {
  const plot = plotRect();
  const bandCol = bandColumnRect();

  return (
    <div style={{ width: '100%', marginTop: 12 }}>
      <svg
        viewBox={`0 0 ${CHART.width} ${CHART.height}`}
        role="img"
        aria-label="OET score chart"
        style={{ width: '100%', height: 'auto', display: 'block' }}
      >
        <title>OET Score Chart</title>
        <desc>
          Listening {scores.listening}, Reading {scores.reading}, Speaking {scores.speaking}, Writing {scores.writing}.
        </desc>

        {/* Subtest column backgrounds */}
        {SUBTESTS.map((s, i) => {
          const col = subtestColumnX(i);
          return (
            <rect
              key={`bg-${s}`}
              x={col.x}
              y={plot.y}
              width={col.w}
              height={plot.h}
              fill="#E8E8E8"
            />
          );
        })}

        {/* Subtest column labels (above chart) */}
        {SUBTESTS.map((s, i) => {
          const col = subtestColumnX(i);
          return (
            <text
              key={`lbl-${s}`}
              x={col.x + col.w / 2}
              y={plot.y - 6}
              fontFamily="Arial, Helvetica, sans-serif"
              fontSize="13"
              fill="#555"
              textAnchor="middle"
            >
              {SUBTEST_LABELS[s]}
            </text>
          );
        })}

        {/* Y-axis ticks + labels */}
        {CHART.ticks.map((t) => {
          const y = scoreToY(t);
          return (
            <g key={`tick-${t}`}>
              <line x1={plot.x - 6} y1={y} x2={plot.x} y2={y} stroke="#999" strokeWidth={1} />
              <text
                x={plot.x - 10}
                y={y + 4}
                fontFamily="Arial, Helvetica, sans-serif"
                fontSize="11"
                fill="#555"
                textAnchor="end"
              >
                {t}
              </text>
            </g>
          );
        })}

        {/* Band column (letter strip) */}
        {BANDS.map((b) => {
          const top = scoreToY(b.max);
          const bottom = scoreToY(b.min);
          const h = bottom - top;
          return (
            <g key={`band-${b.grade}`}>
              <rect
                x={bandCol.x}
                y={top}
                width={bandCol.w}
                height={h}
                fill={b.fill}
              />
              <text
                x={bandCol.x + bandCol.w / 2}
                y={top + h / 2 + 4}
                fontFamily="Arial, Helvetica, sans-serif"
                fontSize={b.grade === 'C+' ? 11 : 13}
                fontWeight="700"
                fill={b.labelFill}
                textAnchor="middle"
              >
                {b.grade}
              </text>
            </g>
          );
        })}

        {/* Dashed threshold lines across subtest columns */}
        {CHART.thresholds.map((t) => {
          const y = scoreToY(t);
          const first = subtestColumnX(0);
          const last = subtestColumnX(3);
          return (
            <line
              key={`thr-${t}`}
              x1={first.x}
              y1={y}
              x2={last.x + last.w}
              y2={y}
              stroke="#1487BF"
              strokeWidth={1.5}
              strokeDasharray="8 6"
            />
          );
        })}

        {/* Score bars */}
        {SUBTESTS.map((s, i) => {
          const col = subtestColumnX(i);
          const score = scores[s];
          const y = scoreToY(score);
          const inside = labelFitsInside(score);
          const labelY = inside ? y + 4 : y - CHART.barHeight / 2 - 4;
          return (
            <g key={`bar-${s}`}>
              <rect
                x={col.x}
                y={y - CHART.barHeight / 2}
                width={col.w}
                height={CHART.barHeight}
                fill="#1487BF"
              />
              <text
                x={col.x + col.w / 2}
                y={labelY}
                fontFamily="Arial, Helvetica, sans-serif"
                fontSize="13"
                fontWeight="700"
                fill={inside ? '#ffffff' : '#1487BF'}
                textAnchor="middle"
              >
                {score}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// Accessible fallback table for screen readers
// ─────────────────────────────────────────────────────────────────────────

function AccessibleTable({ data }: { data: OetStatementOfResults }) {
  return (
    <table className="sr-only">
      <caption>OET Statement of Results — data table</caption>
      <thead>
        <tr>
          <th scope="col">Subtest</th>
          <th scope="col">Score (0–500)</th>
          <th scope="col">Grade</th>
        </tr>
      </thead>
      <tbody>
        {SUBTESTS.map((s) => (
          <tr key={s}>
            <th scope="row">{SUBTEST_LABELS[s]}</th>
            <td>{data.scores[s]}</td>
            <td>{oetGradeFromScaled(data.scores[s])}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// Certification stamp + signature (§5)
// ─────────────────────────────────────────────────────────────────────────

function CertificationBlock() {
  return (
    <div style={certificationBlock} className="oet-sor-cert" aria-hidden="true">
      <OetStamp />
      <div style={{ marginTop: 4 }}>
        {/* eslint-disable-next-line @next/next/no-img-element -- fixed-size document asset, next/image would add layout cost without benefit */}
        <img
          src="/oet/signature-sujata-stead.png"
          alt=""
          width={160}
          height={54}
          style={{ display: 'block' }}
          // if the signature asset is missing, render a subtle placeholder
          onError={(e) => { (e.currentTarget as HTMLImageElement).style.visibility = 'hidden'; }}
        />
      </div>
      <div style={{ fontSize: 13, fontWeight: 700, color: '#333' }}>Sujata Stead</div>
      <div style={{ fontSize: 12, color: '#555' }}>CEO, CBLA</div>
    </div>
  );
}

function OetStamp() {
  // 140×140 SVG recreating the circular CBLA / OET stamp. Grey tones only.
  return (
    <svg
      width={140}
      height={140}
      viewBox="0 0 140 140"
      role="img"
      aria-label="OET / CBLA stamp"
    >
      <defs>
        <path id="stamp-top" d="M 70 70 m -56 0 a 56 56 0 1 1 112 0" fill="none" />
        <path id="stamp-bottom" d="M 70 70 m -56 0 a 56 56 0 1 0 112 0" fill="none" />
      </defs>
      <circle cx={70} cy={70} r={66} fill="#f3f3f3" stroke="#9a9a9a" strokeWidth={1.5} />
      <circle cx={70} cy={70} r={56} fill="#ffffff" stroke="#9a9a9a" strokeWidth={1} />
      <circle cx={70} cy={70} r={32} fill="#f7f7f7" stroke="#b8b8b8" strokeWidth={1.5} />
      <text fill="#4d4d4d" fontFamily="Arial, Helvetica, sans-serif" fontSize="11" letterSpacing="2">
        <textPath href="#stamp-top" startOffset="8%">CAMBRIDGE · BOXHILL</textPath>
      </text>
      <text fill="#4d4d4d" fontFamily="Arial, Helvetica, sans-serif" fontSize="11" letterSpacing="2">
        <textPath href="#stamp-bottom" startOffset="8%">ASSESSMENT · LANGUAGE</textPath>
      </text>
      <text
        x={70}
        y={78}
        textAnchor="middle"
        fontFamily="Arial, Helvetica, sans-serif"
        fontSize="24"
        fontWeight="700"
        fill="#5f5f5f"
      >
        OET
      </text>
    </svg>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// Footers (§6)
// ─────────────────────────────────────────────────────────────────────────

function Footers({ isPractice, suppress }: { isPractice: boolean; suppress: boolean }) {
  return (
    <footer style={footer}>
      <p style={footerParagraph}>
        Recognising organisations are required to validate this Statement of Results through our verification portal.{' '}
        <span style={{ whiteSpace: 'nowrap' }}>
          https://www.occupationalenglishtest.org/organisations/results-verification/
        </span>
      </p>
      <p style={footerParagraph}>
        OET is owned by Cambridge Boxhill Language Assessment Trust (CBLA), a venture between Cambridge Assessment English and Box Hill Institute.
      </p>
      {isPractice && !suppress && (
        <p style={footerDisclaimer}>
          This is a practice result generated by the OET Prep platform. It is not an official OET Statement of Results.
        </p>
      )}
    </footer>
  );
}

// ─────────────────────────────────────────────────────────────────────────
// Inline styles (keep everything self-contained so print is identical)
// ─────────────────────────────────────────────────────────────────────────

const rootStyle: CSSProperties = {
  width: '100%',
  maxWidth: 860,
  margin: '0 auto',
  background: '#ffffff',
  color: '#333',
  fontFamily: 'Arial, Helvetica, sans-serif',
  padding: 24,
  border: '1px solid #e0e0e0',
  borderRadius: 4,
};

const sectionHeaderGrey: CSSProperties = {
  background: '#9A9A9A',
  color: '#ffffff',
  textTransform: 'uppercase',
  fontWeight: 700,
  fontSize: 14,
  padding: '6px 12px',
  letterSpacing: 0.5,
};

const sectionHeaderBlue: CSSProperties = {
  background: '#1D96D2',
  color: '#ffffff',
  textTransform: 'uppercase',
  fontWeight: 700,
  fontSize: 14,
  padding: '6px 12px',
  letterSpacing: 0.5,
  marginTop: 8,
};

const detailsTable: CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
  tableLayout: 'fixed',
};

const detailsKey: CSSProperties = {
  width: '40%',
  background: '#E5E5E5',
  textAlign: 'left',
  fontWeight: 400,
  fontSize: 13,
  padding: '8px 12px',
  color: '#333',
};

const detailsVal: CSSProperties = {
  width: '60%',
  background: '#ffffff',
  textAlign: 'left',
  fontSize: 13,
  fontWeight: 500,
  padding: '8px 12px',
  color: '#333',
};

const stripRow: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(4, 1fr)',
  background: '#1487BF',
};

const stripHeaderCell: CSSProperties = {
  color: '#ffffff',
  fontSize: 15,
  fontWeight: 700,
  padding: '10px 12px',
  textAlign: 'left',
};

const stripValueCell: CSSProperties = {
  color: '#ffffff',
  fontSize: 22,
  fontWeight: 700,
  padding: '8px 12px',
  textAlign: 'center',
};

const certificationBlock: CSSProperties = {
  marginTop: 24,
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'flex-end',
  gap: 0,
};

const footer: CSSProperties = {
  marginTop: 16,
  borderTop: '1px solid #e0e0e0',
  paddingTop: 12,
};

const footerParagraph: CSSProperties = {
  fontSize: 11,
  color: '#666',
  margin: '4px 0',
  lineHeight: 1.5,
};

const footerDisclaimer: CSSProperties = {
  fontSize: 11,
  color: '#8A6D00',
  background: '#FFF8E1',
  border: '1px solid #FFE58F',
  borderRadius: 4,
  padding: '6px 10px',
  margin: '8px 0 0 0',
  fontStyle: 'italic',
};
