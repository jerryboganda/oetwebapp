import { describe, it, expect } from 'vitest';
import {
  cleanListeningPrompt,
  cleanListeningOption,
  sanitizeListeningPrompt,
  sanitizeListeningOption,
  isSentinelPrompt,
} from '@/lib/listening-question-clean';

describe('listening-question-clean', () => {
  describe('cleanListeningPrompt', () => {
    it('strips extraction page markers and headers', () => {
      const raw = '====== PAGE 4 ======\nPractice Test 1\nWhat is the nurse discussing with the patient?';
      expect(cleanListeningPrompt(raw)).toBe('What is the nurse discussing with the patient?');
      expect(sanitizeListeningPrompt(raw)).toBe('What is the nurse discussing with the patient?');
    });

    it('strips standalone PAGE markers and practice test markers', () => {
      expect(cleanListeningPrompt('PAGE 5\nWhat is the nurse discussing with the patient?')).toBe(
        'What is the nurse discussing with the patient?',
      );
      expect(cleanListeningPrompt('Practice Test 1\nWhat is the nurse discussing with the patient?')).toBe(
        'What is the nurse discussing with the patient?',
      );
    });

    it('returns empty string for sentinels', () => {
      expect(cleanListeningPrompt('See PDF')).toBe('');
      expect(cleanListeningPrompt('CPDF')).toBe('');
      expect(cleanListeningPrompt('PDF')).toBe('');
      expect(cleanListeningPrompt('View PDF')).toBe('');
      expect(isSentinelPrompt('See PDF')).toBe(true);
      expect(isSentinelPrompt('cpdf')).toBe(true);
      expect(isSentinelPrompt('What is the diagnosis?')).toBe(false);
    });

    it('returns empty string for artifact-only prompts', () => {
      expect(cleanListeningPrompt('====== PAGE 4 ======')).toBe('');
      expect(cleanListeningPrompt('PAGE 5')).toBe('');
      expect(cleanListeningPrompt('Practice Test 1')).toBe('');
    });

    it('strips inline page markers and practice test prefixes', () => {
      expect(cleanListeningPrompt('PAGE 4 Question 25 What is the patient condition?')).toBe(
        'Question 25 What is the patient condition?',
      );
      expect(cleanListeningPrompt('Practice Test 1 : You hear a doctor talking to a nurse.')).toBe(
        'You hear a doctor talking to a nurse.',
      );
    });

    it('preserves legitimate clinical and medical words', () => {
      expect(cleanListeningPrompt('In general practice, hypertension is common.')).toBe(
        'In general practice, hypertension is common.',
      );
      expect(cleanListeningPrompt('The patient presented with Paget disease.')).toBe(
        'The patient presented with Paget disease.',
      );
    });
  });

  describe('cleanListeningOption', () => {
    it('strips placeholder option texts', () => {
      expect(cleanListeningOption('Option A')).toBe('');
      expect(cleanListeningOption('Option B')).toBe('');
      expect(cleanListeningOption('Option C')).toBe('');
    });

    it('strips sentinels from options', () => {
      expect(cleanListeningOption('See PDF')).toBe('');
      expect(cleanListeningOption('CPDF')).toBe('');
    });

    it('cleans extraction artifacts from options', () => {
      expect(cleanListeningOption('====== PAGE 4 ======\nTake 500mg paracetamol')).toBe(
        'Take 500mg paracetamol',
      );
      expect(cleanListeningOption('Take 500mg paracetamol orally')).toBe(
        'Take 500mg paracetamol orally',
      );
    });
  });
});
