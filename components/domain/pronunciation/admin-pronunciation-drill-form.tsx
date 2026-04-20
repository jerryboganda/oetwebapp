import { Card } from '@/components/ui/card';

export type DrillForm = {
  word: string;
  phoneticTranscription: string;
  profession: string;
  focus: string;
  primaryRuleId: string;
  difficulty: string;
  tipsHtml: string;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  audioUrl: string;
};

type PronunciationDrillFormProps = {
  form: DrillForm;
  onChange: (next: DrillForm) => void;
};

export function PronunciationDrillForm({ form, onChange }: PronunciationDrillFormProps) {
  function set<K extends keyof DrillForm>(key: K, value: DrillForm[K]) {
    onChange({ ...form, [key]: value });
  }

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <Card className="p-5 space-y-3">
        <h2 className="text-sm font-semibold uppercase tracking-[0.15em] text-muted">Metadata</h2>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Label <span className="text-rose-500">*</span></span>
          <input
            type="text"
            value={form.word}
            onChange={(event) => set('word', event.target.value)}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="e.g. th (voiceless) - as in 'think'"
            required
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Target phoneme (IPA) <span className="text-rose-500">*</span></span>
          <input
            type="text"
            value={form.phoneticTranscription}
            onChange={(event) => set('phoneticTranscription', event.target.value)}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="theta, eth, v, short i, ash, stress, intonation"
          />
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Profession</span>
            <select
              value={form.profession}
              onChange={(event) => set('profession', event.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="all">All</option>
              <option value="medicine">Medicine</option>
              <option value="nursing">Nursing</option>
              <option value="dentistry">Dentistry</option>
              <option value="pharmacy">Pharmacy</option>
              <option value="physiotherapy">Physiotherapy</option>
              <option value="occupational-therapy">Occupational therapy</option>
              <option value="speech-pathology">Speech pathology</option>
            </select>
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Focus</span>
            <select
              value={form.focus}
              onChange={(event) => set('focus', event.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="phoneme">Phoneme</option>
              <option value="cluster">Cluster</option>
              <option value="stress">Word stress</option>
              <option value="intonation">Intonation</option>
              <option value="prosody">Prosody</option>
            </select>
          </label>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Difficulty</span>
            <select
              value={form.difficulty}
              onChange={(event) => set('difficulty', event.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="easy">Easy</option>
              <option value="medium">Medium</option>
              <option value="hard">Hard</option>
            </select>
          </label>
          <label className="block">
            <span className="text-sm text-navy dark:text-white">Primary rule ID</span>
            <input
              type="text"
              value={form.primaryRuleId}
              onChange={(event) => set('primaryRuleId', event.target.value)}
              className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              placeholder="e.g. P01.1"
            />
          </label>
        </div>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Model audio URL</span>
          <input
            type="url"
            value={form.audioUrl}
            onChange={(event) => set('audioUrl', event.target.value)}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="https://example.com/model.mp3"
          />
        </label>
      </Card>

      <Card className="p-5 space-y-3">
        <h2 className="text-sm font-semibold uppercase tracking-[0.15em] text-muted">Content</h2>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Tips (HTML)</span>
          <textarea
            value={form.tipsHtml}
            onChange={(event) => set('tipsHtml', event.target.value)}
            rows={4}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder="<p>...</p>"
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Example words (JSON array of strings)</span>
          <textarea
            value={form.exampleWordsJson}
            onChange={(event) => set('exampleWordsJson', event.target.value)}
            rows={3}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder='["think","therapy","three"]'
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Minimal pairs (JSON)</span>
          <textarea
            value={form.minimalPairsJson}
            onChange={(event) => set('minimalPairsJson', event.target.value)}
            rows={3}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder='[{"a":"think","b":"sink"}]'
          />
        </label>
        <label className="block">
          <span className="text-sm text-navy dark:text-white">Practice sentences (JSON array of strings)</span>
          <textarea
            value={form.sentencesJson}
            onChange={(event) => set('sentencesJson', event.target.value)}
            rows={3}
            className="mt-1 w-full rounded-xl border border-border bg-surface px-3 py-2 font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary"
            placeholder='["Please breathe deeply through the mouth."]'
          />
        </label>
      </Card>
    </div>
  );
}
