/**
 * ============================================================================
 * Writing Drills — Loader
 * ============================================================================
 *
 * Static-imports the seeded drill JSON files, validates them against the
 * zod schema, and exposes typed lookup helpers. Pattern matches
 * `lib/rulebook/loader.ts`.
 *
 * Adding a new drill:
 *   1. Drop a JSON file under `rulebooks/writing/drills/<profession>/<type>/`.
 *   2. Add a static import + `register(...)` call below.
 *   3. Validation runs at module load — invalid drills throw immediately.
 * ============================================================================
 */

import medicineRelevance001 from '../../rulebooks/writing/drills/medicine/relevance/relevance-001.json';
import medicineOpening001 from '../../rulebooks/writing/drills/medicine/opening/opening-001.json';
import medicineOrdering001 from '../../rulebooks/writing/drills/medicine/ordering/ordering-001.json';
import medicineExpansion001 from '../../rulebooks/writing/drills/medicine/expansion/expansion-001.json';
import medicineTone001 from '../../rulebooks/writing/drills/medicine/tone/tone-001.json';
import medicineAbbreviation001 from '../../rulebooks/writing/drills/medicine/abbreviation/abbreviation-001.json';

import {
  DrillSchema,
  toDrillSummary,
  type Drill,
  type DrillSummary,
  type DrillType,
  type Profession,
} from './types';

const RAW_REGISTRY: unknown[] = [
  medicineRelevance001,
  medicineOpening001,
  medicineOrdering001,
  medicineExpansion001,
  medicineTone001,
  medicineAbbreviation001,
];

let registry: Map<string, Drill> | null = null;

function buildRegistry(): Map<string, Drill> {
  const map = new Map<string, Drill>();
  for (const raw of RAW_REGISTRY) {
    const parsed = DrillSchema.safeParse(raw);
    if (!parsed.success) {
      const issues = parsed.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
      const id = (raw as { id?: string })?.id ?? '<unknown>';
      throw new Error(`[writing-drills] Invalid drill "${id}": ${issues}`);
    }
    if (map.has(parsed.data.id)) {
      throw new Error(`[writing-drills] Duplicate drill id: ${parsed.data.id}`);
    }
    map.set(parsed.data.id, parsed.data);
  }
  return map;
}

function getRegistry(): Map<string, Drill> {
  if (!registry) registry = buildRegistry();
  return registry;
}

export class DrillNotFoundError extends Error {
  constructor(id: string) {
    super(`Writing drill not found: ${id}`);
    this.name = 'DrillNotFoundError';
  }
}

export function listDrills(filter?: {
  type?: DrillType;
  profession?: Profession;
}): DrillSummary[] {
  const all = Array.from(getRegistry().values());
  return all
    .filter((d) => (filter?.type ? d.type === filter.type : true))
    .filter((d) => (filter?.profession ? d.profession === filter.profession : true))
    .map(toDrillSummary);
}

export function getDrill(id: string): Drill {
  const drill = getRegistry().get(id);
  if (!drill) throw new DrillNotFoundError(id);
  return drill;
}

export function getDrillsByType(type: DrillType, profession?: Profession): Drill[] {
  return Array.from(getRegistry().values())
    .filter((d) => d.type === type)
    .filter((d) => (profession ? d.profession === profession : true));
}
