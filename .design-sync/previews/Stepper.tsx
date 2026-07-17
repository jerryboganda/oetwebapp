// Authored preview — Stepper. Each named export = one labeled card cell.
import { Stepper } from 'oet-with-dr-hesham';

const examSteps = [
  { id: 'instructions', label: 'Instructions' },
  { id: 'part-a', label: 'Part A' },
  { id: 'part-b', label: 'Part B' },
  { id: 'review', label: 'Review' },
];

export const InProgress = () => (
  <div style={{ maxWidth: 640 }}>
    {/* currentStep is 0-indexed: Part B active, earlier steps complete. */}
    <Stepper steps={examSteps} currentStep={2} />
  </div>
);

export const AtStart = () => (
  <div style={{ maxWidth: 640 }}>
    <Stepper steps={examSteps} currentStep={0} />
  </div>
);

export const Vertical = () => (
  <div style={{ maxWidth: 360 }}>
    <Stepper
      orientation="vertical"
      currentStep={1}
      steps={[
        { id: 'register', label: 'Account created', description: 'Profession and target grade set' },
        { id: 'placement', label: 'Choose your plan', description: 'Pick a study pathway and start date' },
        { id: 'study', label: 'Begin practice', description: 'Listening, Reading, Writing and Speaking' },
        { id: 'mock', label: 'Sit a full mock', description: 'Timed exam under test conditions' },
      ]}
    />
  </div>
);
