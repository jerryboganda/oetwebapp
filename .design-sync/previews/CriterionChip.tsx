// Authored preview — CriterionChip. Each named export = one labeled card cell.
import { CriterionChip } from 'oet-with-dr-hesham';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>{children}</div>
);

export const SpeakingCriteria = () => (
  <Row>
    <CriterionChip label="Intelligibility" active />
    <CriterionChip label="Fluency" />
    <CriterionChip label="Appropriateness of language" />
    <CriterionChip label="Resources of grammar & expression" />
  </Row>
);

export const WritingCriteria = () => (
  <Row>
    <CriterionChip label="Purpose" />
    <CriterionChip label="Content" active />
    <CriterionChip label="Conciseness & clarity" />
    <CriterionChip label="Genre & style" />
    <CriterionChip label="Organisation & layout" />
    <CriterionChip label="Language" />
  </Row>
);

export const ActiveVsInactive = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    <Row>
      <CriterionChip label="Grammar" active />
      <CriterionChip label="Vocabulary" active />
    </Row>
    <Row>
      <CriterionChip label="Grammar" />
      <CriterionChip label="Vocabulary" />
    </Row>
  </div>
);
