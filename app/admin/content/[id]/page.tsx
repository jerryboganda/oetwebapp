'use client';

import { useState } from 'react';
import { useRouter, useParams } from 'next/navigation';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Save, CheckCircle, ArrowLeft, History } from 'lucide-react';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { analytics } from '@/lib/analytics';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

export default function TaskBuilderPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const router = useRouter();
  const params = useParams();
  const isNew = !params?.id;
  const [activeTab, setActiveTab] = useState('metadata');
  const [isSaving, setIsSaving] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  if (!isAuthenticated || role !== 'admin') return null;

  const handleSave = async (publish: boolean) => {
    setIsSaving(true);
    try {
      await new Promise(resolve => setTimeout(resolve, 800));
      if (publish) {
        analytics.track(isNew ? 'admin_content_created' : 'admin_content_published', { contentId: params?.id as string });
        setToast({ variant: 'success', message: 'Content published successfully.' });
      } else {
        analytics.track(isNew ? 'admin_content_created' : 'admin_content_updated', { contentId: params?.id as string });
        setToast({ variant: 'success', message: 'Draft saved.' });
      }
    } catch {
      setToast({ variant: 'error', message: 'Failed to save. Please try again.' });
    } finally {
      setIsSaving(false);
    }
  };

  const tabs = [
    { id: 'metadata', label: 'Metadata & Content' },
    { id: 'criteria', label: 'Criteria Mapping' },
    { id: 'rubric', label: 'Model Answer & Rubric' },
  ];

  return (
    <div className="max-w-5xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label={isNew ? 'Create New Task' : 'Edit Content'}>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" size="sm" onClick={() => router.back()} className="px-2">
            <ArrowLeft className="w-5 h-5" />
          </Button>
          <div>
            <h1 className="text-2xl font-bold text-navy tracking-tight">
              {isNew ? 'Create New Task' : `Edit Content ${params?.id}`}
            </h1>
            <p className="text-sm text-muted mt-1">Configure metadata, mapping, and reference answers.</p>
          </div>
        </div>
        <div className="flex items-center gap-3">
          {!isNew && (
            <Button variant="outline" size="sm" onClick={() => router.push(`/admin/content/${params?.id}/revisions`)} className="gap-2">
              <History className="w-4 h-4" /> Revisions
            </Button>
          )}
          <Button variant="outline" onClick={() => handleSave(false)} loading={isSaving} className="gap-2 bg-surface">
            <Save className="w-4 h-4" /> Save Draft
          </Button>
          <Button onClick={() => handleSave(true)} loading={isSaving} className="gap-2">
            <CheckCircle className="w-4 h-4" /> Publish
          </Button>
        </div>
      </div>

      <div className="mb-6 bg-surface px-2 pt-2 rounded-t-xl border-b border-gray-200">
        <Tabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} className="border-none" />
      </div>

      <TabPanel id="metadata" activeTab={activeTab}>
        <Card>
          <CardHeader><CardTitle>Core Details</CardTitle></CardHeader>
          <CardContent className="space-y-5">
            <Input label="Task Title" placeholder="e.g., Cardiology Referral Letter" />
            <div className="grid grid-cols-2 gap-4">
              <Select label="Task Type" options={[
                { value: 'writing', label: 'Writing Task' },
                { value: 'speaking', label: 'Speaking Roleplay' },
                { value: 'reading', label: 'Reading Part A' },
                { value: 'listening', label: 'Listening Part C' },
              ]} />
              <Select label="Profession Target" options={[
                { value: 'all', label: 'All Professions' },
                { value: 'medicine', label: 'Medicine' },
                { value: 'nursing', label: 'Nursing' },
                { value: 'dentistry', label: 'Dentistry' },
              ]} />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <Select label="Difficulty" options={[
                { value: 'easy', label: 'Easy' },
                { value: 'medium', label: 'Medium' },
                { value: 'hard', label: 'Hard' },
              ]} />
              <Input label="Estimated Duration (minutes)" type="number" placeholder="45" />
            </div>
            <Textarea label="Prompt / Case Notes" placeholder="Enter the exact prompt text or case notes the learner will see..." className="min-h-[250px]" />
          </CardContent>
        </Card>
      </TabPanel>

      <TabPanel id="criteria" activeTab={activeTab}>
        <Card>
          <CardHeader><CardTitle>Scoring Configuration</CardTitle></CardHeader>
          <CardContent className="space-y-4">
            <p className="text-sm text-muted mb-4">Map this task to official OET criteria to enable AI and human evaluation.</p>
            <div className="p-8 border border-dashed border-gray-300 rounded-lg text-center bg-gray-50/50">
              <p className="text-muted font-medium">Select a Task Type first to load available criteria templates.</p>
            </div>
          </CardContent>
        </Card>
      </TabPanel>

      <TabPanel id="rubric" activeTab={activeTab}>
        <Card>
          <CardHeader><CardTitle>Reference Materials</CardTitle></CardHeader>
          <CardContent className="space-y-5">
            <Textarea label="Model Answer" placeholder="Provide a high-scoring reference answer..." className="min-h-[200px]" />
            <Textarea label="Internal Rubric Notes" placeholder="Notes for expert reviewers (not visible to learners)..." className="min-h-[150px]" />
          </CardContent>
        </Card>
      </TabPanel>
    </div>
  );
}
