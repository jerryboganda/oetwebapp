'use client';

import { useCallback, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Users, Plus, UserPlus, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchStudyGroups, createStudyGroup, joinStudyGroup } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type StudyGroup = { id: string; name: string; description: string; examTypeCode: string; memberCount: number; maxMembers: number; createdAt: string };

export default function StudyGroupsPage() {
  const [groups, setGroups] = useState<StudyGroup[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [examType, setExamType] = useState('');
  const [joining, setJoining] = useState<string | null>(null);
  const [joined, setJoined] = useState<Set<string>>(new Set());
  const [showCreate, setShowCreate] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newGroup, setNewGroup] = useState({ name: '', description: '', examTypeCode: 'oet', isPublic: true });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await fetchStudyGroups(examType || undefined) as { groups: StudyGroup[]; total: number };
      setGroups(data.groups ?? []);
      setTotal(data.total ?? 0);
    } catch {
      setError('Could not load study groups.');
    } finally {
      setLoading(false);
    }
  }, [examType]);

  useEffect(() => {
    analytics.track('study_groups_viewed');
    void load();
  }, [load]);

  async function handleJoin(groupId: string) {
    if (joining || joined.has(groupId)) return;
    setJoining(groupId);
    try {
      await joinStudyGroup(groupId);
      setJoined(prev => new Set(prev).add(groupId));
      setGroups(prev => prev.map(g => g.id === groupId ? { ...g, memberCount: g.memberCount + 1 } : g));
    } catch {
      setError('Could not join group.');
    } finally {
      setJoining(null);
    }
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!newGroup.name.trim()) return;
    setCreating(true);
    try {
      await createStudyGroup({ name: newGroup.name, description: newGroup.description, examTypeCode: newGroup.examTypeCode, isPublic: newGroup.isPublic });
      setShowCreate(false);
      setNewGroup({ name: '', description: '', examTypeCode: 'oet', isPublic: true });
      await load();
    } catch {
      setError('Could not create group.');
    } finally {
      setCreating(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Study Groups"
          description="Join or create a group to study together"
          icon={Users}
        />
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-sm font-medium transition-colors"
        >
          <Plus className="w-4 h-4" /> Create Group
        </button>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Create group form */}
      {showCreate && (
        <motion.div initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }} className="bg-white dark:bg-gray-800 rounded-xl border border-indigo-200 dark:border-indigo-700 p-5 mb-6">
          <h3 className="font-semibold text-gray-900 dark:text-white mb-4">Create New Study Group</h3>
          <form onSubmit={handleCreate} className="space-y-3">
            <input
              type="text"
              placeholder="Group name"
              value={newGroup.name}
              onChange={e => setNewGroup(p => ({ ...p, name: e.target.value }))}
              required
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
            <textarea
              placeholder="Description (optional)"
              value={newGroup.description}
              onChange={e => setNewGroup(p => ({ ...p, description: e.target.value }))}
              rows={2}
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 resize-none focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
            <div className="flex gap-3">
              <select
                value={newGroup.examTypeCode}
                onChange={e => setNewGroup(p => ({ ...p, examTypeCode: e.target.value }))}
                className="flex-1 px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-700 dark:text-gray-300"
              >
                <option value="oet">OET</option>
                <option value="ielts">IELTS</option>
                <option value="pte">PTE</option>
              </select>
              <label className="flex items-center gap-2 text-sm text-gray-600 dark:text-gray-400">
                <input type="checkbox" checked={newGroup.isPublic} onChange={e => setNewGroup(p => ({ ...p, isPublic: e.target.checked }))} className="rounded" />
                Public
              </label>
            </div>
            <div className="flex gap-2 pt-1">
              <button type="submit" disabled={creating} className="px-5 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-sm font-medium disabled:opacity-50">
                {creating ? 'Creating...' : 'Create Group'}
              </button>
              <button type="button" onClick={() => setShowCreate(false)} className="px-5 py-2 border border-gray-300 dark:border-gray-600 rounded-lg text-sm text-gray-600 dark:text-gray-400">
                Cancel
              </button>
            </div>
          </form>
        </motion.div>
      )}

      {/* Filters */}
      <div className="flex gap-3 mb-6">
        <select value={examType} onChange={e => setExamType(e.target.value)} className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300">
          <option value="">All Exams</option>
          <option value="oet">OET</option>
          <option value="ielts">IELTS</option>
          <option value="pte">PTE</option>
        </select>
      </div>

      {loading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-36 rounded-xl" />)}
        </div>
      ) : groups.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No study groups found. Create the first one!</div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {groups.map((group, i) => (
            <motion.div
              key={group.id}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: i * 0.04 }}
              className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-5"
            >
              <div className="flex items-start justify-between mb-2">
                <div className="flex-1 min-w-0">
                  <div className="font-semibold text-gray-900 dark:text-white">{group.name}</div>
                  {group.description && <div className="text-sm text-gray-500 mt-0.5 line-clamp-2">{group.description}</div>}
                </div>
                <span className="text-xs px-2 py-0.5 rounded-full bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 uppercase ml-2 flex-shrink-0">{group.examTypeCode}</span>
              </div>
              <div className="flex items-center justify-between mt-3">
                <div className="flex items-center gap-1.5 text-sm text-gray-500">
                  <Users className="w-4 h-4" />
                  {group.memberCount}/{group.maxMembers} members
                </div>
                <button
                  onClick={() => handleJoin(group.id)}
                  disabled={joining === group.id || joined.has(group.id) || group.memberCount >= group.maxMembers}
                  className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${joined.has(group.id) ? 'bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-300' : group.memberCount >= group.maxMembers ? 'bg-gray-100 dark:bg-gray-700 text-gray-400 cursor-not-allowed' : 'bg-indigo-600 hover:bg-indigo-700 text-white'}`}
                >
                  {joined.has(group.id) ? 'Joined' : group.memberCount >= group.maxMembers ? <><Lock className="w-3.5 h-3.5" /> Full</> : <><UserPlus className="w-3.5 h-3.5" /> Join</>}
                </button>
              </div>
            </motion.div>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
