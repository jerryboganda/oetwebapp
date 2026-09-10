'use client';

import { useEffect, useRef, useState, type ChangeEvent, type KeyboardEvent } from 'react';
import { Send, Square, Paperclip, X, FileText, Image as ImageIcon, Mic } from 'lucide-react';
import type { AssistantAttachmentInput } from '@/hooks/use-ai-assistant';

export interface AiAssistantInputProps {
  onSend: (content: string, attachments?: AssistantAttachmentInput) => void;
  onCancel: () => void;
  isStreaming: boolean;
  disabled?: boolean;
}

interface PendingImage {
  fileName: string;
  mimeType: string;
  bytes: Uint8Array;
  previewUrl: string;
}

interface PendingDocument {
  fileName: string;
  mimeType: string;
  text: string;
}

/** Images the provider can see natively (vision / ubag_attachments). */
const IMAGE_MIMES = new Set(['image/jpeg', 'image/png', 'image/gif', 'image/webp']);
const IMAGE_EXTENSIONS = new Set(['jpg', 'jpeg', 'png', 'gif', 'webp']);
/** Documents are text-extracted client-side and folded into the prompt. */
const DOCUMENT_EXTENSIONS = new Set(['pdf', 'txt', 'md', 'docx']);
const MAX_IMAGES = 3;
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const MAX_DOCUMENT_CHARS = 60000;
/** Speech is bulky; twenty megabytes is roughly twenty minutes compressed. */
const MAX_AUDIO_BYTES = 20 * 1024 * 1024;

export function AiAssistantInput({ onSend, onCancel, isStreaming, disabled = false }: AiAssistantInputProps) {
  const [value, setValue] = useState('');
  const [images, setImages] = useState<PendingImage[]>([]);
  const [document, setDocument] = useState<PendingDocument | null>(null);
  const [attachError, setAttachError] = useState<string | null>(null);
  const [extracting, setExtracting] = useState(false);
  const [recording, setRecording] = useState(false);
  const [voiceNote, setVoiceNote] = useState<{ bytes: Uint8Array; mimeType: string } | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const imagesRef = useRef<PendingImage[]>(images);
  imagesRef.current = images;

  // A live microphone stream is not something to leave running because a
  // component unmounted mid-recording.
  useEffect(() => () => stopTracks(recorderRef.current), []);

  // Pending image previews are object URLs; if the panel closes before the
  // user sends or removes them, revoke whatever is still outstanding.
  useEffect(() => () => {
    for (const img of imagesRef.current) URL.revokeObjectURL(img.previewUrl);
  }, []);

  const busy = disabled || isStreaming || extracting || recording;
  const canSend =
    (value.trim().length > 0 || images.length > 0 || document !== null || voiceNote !== null) && !busy;

  const handleSend = () => {
    const trimmed = value.trim();
    if ((!trimmed && images.length === 0 && !document && !voiceNote) || busy) return;
    onSend(trimmed, {
      images: images.map((img) => ({ bytes: img.bytes, mimeType: img.mimeType, fileName: img.fileName })),
      document: document ? { fileName: document.fileName, mimeType: document.mimeType, text: document.text } : undefined,
      audio: voiceNote ?? undefined,
    });
    setValue('');
    for (const img of images) URL.revokeObjectURL(img.previewUrl);
    setImages([]);
    setDocument(null);
    setVoiceNote(null);
    setAttachError(null);
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleFiles = async (e: ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0 || busy) return;
    setAttachError(null);
    // Local running count -- `images` is a snapshot from this render and
    // never changes across iterations of this loop, so the cap has to be
    // tracked here rather than re-read from state on every file.
    let pendingCount = images.length;
    for (const file of files) {
      const ext = (file.name.split('.').pop() ?? '').toLowerCase();
      const mime = (file.type || '').toLowerCase();
      if (IMAGE_MIMES.has(mime) || IMAGE_EXTENSIONS.has(ext)) {
        if (pendingCount >= MAX_IMAGES) {
          setAttachError(`Up to ${MAX_IMAGES} images per message.`);
          break;
        }
        if (file.size > MAX_IMAGE_BYTES) {
          setAttachError(`${file.name}: images must be 5 MB or smaller.`);
          continue;
        }
        const buffer = new Uint8Array(await file.arrayBuffer());
        const resolvedMime = IMAGE_MIMES.has(mime) ? mime : ext === 'jpg' || ext === 'jpeg' ? 'image/jpeg' : ext === 'png' ? 'image/png' : ext === 'gif' ? 'image/gif' : 'image/webp';
        pendingCount += 1;
        setImages((prev) =>
          prev.length >= MAX_IMAGES
            ? prev
            : [...prev, { fileName: file.name, mimeType: resolvedMime, bytes: buffer, previewUrl: URL.createObjectURL(file) }],
        );
      } else if (DOCUMENT_EXTENSIONS.has(ext)) {
        setExtracting(true);
        try {
          const text = await extractDocumentText(file, ext);
          const trimmed = text.trim();
          if (!trimmed) {
            setAttachError(`${file.name}: no readable text found.`);
          } else {
            setDocument({
              fileName: file.name,
              mimeType: file.type || (ext === 'pdf' ? 'application/pdf' : 'text/plain'),
              text: trimmed.slice(0, MAX_DOCUMENT_CHARS),
            });
            if (trimmed.length > MAX_DOCUMENT_CHARS) setAttachError('Document truncated to ~60 KB of text.');
          }
        } catch {
          setAttachError(`${file.name}: could not be read.`);
        } finally {
          setExtracting(false);
        }
      } else {
        setAttachError(`${file.name}: supported files are JPG, PNG, GIF, WEBP, PDF, TXT, MD, DOCX.`);
      }
    }
  };

  const startRecording = async () => {
    setAttachError(null);

    // Feature-detect rather than assume: MediaRecorder is absent in some in-app
    // browsers and on older iOS, and the honest failure is "type it instead",
    // not a button that does nothing.
    if (
      typeof navigator === 'undefined' ||
      !navigator.mediaDevices?.getUserMedia ||
      typeof MediaRecorder === 'undefined'
    ) {
      setAttachError('Voice messages are not supported in this browser. Type your message instead.');
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const recorder = new MediaRecorder(stream);
      const parts: BlobPart[] = [];

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) parts.push(event.data);
      };

      recorder.onstop = async () => {
        // This fires however the recorder stopped -- the Stop button, an
        // externally-ended track, or a browser-initiated stop -- so it is
        // the one place that can reliably clear `recording`.
        setRecording(false);
        stopTracks(recorder);
        const blob = new Blob(parts, { type: recorder.mimeType || 'audio/webm' });

        if (blob.size === 0) {
          setAttachError('That recording was empty. Try again.');
          return;
        }
        if (blob.size > MAX_AUDIO_BYTES) {
          setAttachError('That recording is too long. Keep voice notes under 20 MB.');
          return;
        }

        setVoiceNote({
          bytes: new Uint8Array(await blob.arrayBuffer()),
          mimeType: (recorder.mimeType || 'audio/webm').split(';')[0],
        });
      };

      recorder.onerror = () => {
        setRecording(false);
        stopTracks(recorder);
        setAttachError('Voice recording failed. Try again.');
      };

      recorderRef.current = recorder;
      recorder.start();
      setRecording(true);
    } catch {
      // Almost always a denied permission prompt. Saying so is more useful than
      // a generic failure, because the fix is in the browser, not in the app.
      setAttachError('Microphone access was not granted. Type your message instead.');
    }
  };

  const stopRecording = () => {
    try {
      recorderRef.current?.stop();
    } catch {
      // Already inactive (e.g. it auto-stopped once already, or this is a
      // duplicate click) -- onstop won't fire again, so clear the UI here
      // instead of leaving `recording` stuck true.
    } finally {
      setRecording(false);
    }
  };

  const removeImage = (index: number) => {
    setImages((prev) => {
      URL.revokeObjectURL(prev[index].previewUrl);
      return prev.filter((_, i) => i !== index);
    });
  };

  return (
    <div className="border-t border-border p-3">
      {(images.length > 0 || document || voiceNote) && (
        <div className="mb-2 flex flex-wrap gap-2" data-testid="assistant-attachments">
          {images.map((img, i) => (
            <span key={`${img.fileName}-${i}`} className="relative inline-block">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={img.previewUrl} alt={img.fileName} className="h-12 w-12 rounded-lg border border-border object-cover" />
              <button
                onClick={() => removeImage(i)}
                aria-label={`Remove ${img.fileName}`}
                className="absolute -right-1.5 -top-1.5 rounded-full bg-surface p-0.5 shadow"
              >
                <X className="h-3 w-3" />
              </button>
            </span>
          ))}
          {document && (
            <span className="inline-flex max-w-full items-center gap-1.5 rounded-lg border border-border bg-background px-2 py-1 text-xs">
              <FileText className="h-3.5 w-3.5 shrink-0" />
              <span className="truncate">{document.fileName}</span>
              <button onClick={() => setDocument(null)} aria-label={`Remove ${document.fileName}`} className="rounded p-0.5 hover:bg-background-light">
                <X className="h-3 w-3" />
              </button>
            </span>
          )}
          {voiceNote && (
            <span className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-background px-2 py-1 text-xs">
              <Mic className="h-3.5 w-3.5 shrink-0" />
              <span>Voice message ready to send</span>
              <button
                onClick={() => setVoiceNote(null)}
                aria-label="Remove voice message"
                className="rounded p-0.5 hover:bg-background-light"
              >
                <X className="h-3 w-3" />
              </button>
            </span>
          )}
        </div>
      )}
      {attachError && (
        <p role="alert" className="mb-2 text-xs text-red-600">
          {attachError}
        </p>
      )}
      <div className="flex items-end gap-2">
        <input
          ref={fileRef}
          type="file"
          multiple
          accept="image/jpeg,image/png,image/gif,image/webp,.pdf,.txt,.md,.docx"
          onChange={(e) => void handleFiles(e)}
          className="hidden"
          aria-label="Attach file or image"
          data-testid="assistant-file-picker"
        />
        <button
          onClick={() => fileRef.current?.click()}
          disabled={busy}
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-border hover:bg-background-light disabled:opacity-50"
          aria-label="Attach file or image"
          title="Attach an image (JPG/PNG/GIF/WEBP) or document (PDF/TXT/MD/DOCX)"
        >
          <Paperclip className="h-4 w-4" />
        </button>
        <button
          onClick={recording ? stopRecording : () => void startRecording()}
          disabled={disabled || isStreaming || extracting}
          className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-border disabled:opacity-50 ${
            recording ? 'bg-red-500 text-white' : 'hover:bg-background-light'
          }`}
          aria-label={recording ? 'Stop recording' : 'Record a voice message'}
          aria-pressed={recording}
          title={recording ? 'Stop recording' : 'Record a voice message'}
          data-testid="assistant-record"
        >
          {recording ? <Square className="h-4 w-4" /> : <Mic className="h-4 w-4" />}
        </button>
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Type a message..."
          disabled={busy}
          rows={1}
          className="flex-1 resize-none rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary disabled:opacity-50"
          aria-label="Message input"
        />
        {isStreaming ? (
          <button
            onClick={onCancel}
            className="flex h-9 w-9 items-center justify-center rounded-lg bg-red-500 text-white hover:bg-red-600"
            aria-label="Cancel"
          >
            <Square className="h-4 w-4" />
          </button>
        ) : (
          <button
            onClick={handleSend}
            disabled={!canSend}
            className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50"
            aria-label="Send message"
          >
            {extracting ? <ImageIcon className="h-4 w-4 animate-pulse" /> : <Send className="h-4 w-4" />}
          </button>
        )}
      </div>
    </div>
  );
}

async function extractDocumentText(file: File, ext: string): Promise<string> {
  if (ext === 'txt' || ext === 'md') return file.text();
  if (ext === 'docx') return extractDocxText(file);
  return extractPdfText(file);
}

async function extractDocxText(file: File): Promise<string> {
  const { default: JSZip } = await import('jszip');
  const zip = await JSZip.loadAsync(await file.arrayBuffer());
  const xml = await zip.file('word/document.xml')?.async('string');
  if (!xml) return '';
  const texts = Array.from(xml.matchAll(/<w:t[^>]*>([^<]*)<\/w:t>/g)).map((m) => m[1]);
  return texts.join(' ').replace(/\s+/g, ' ').trim();
}

async function extractPdfText(file: File): Promise<string> {
  const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
  // Same worker path every other viewer in this repo uses (mirrors
  // writing stimulus + listening overlay imports: a URL resolved from
  // import.meta.url — never a CDN, never unset). Without it pdf.js throws
  // "No GlobalWorkerOptions.workerSrc" and every PDF upload fails.
  pdfjs.GlobalWorkerOptions.workerSrc = new URL(
    'pdfjs-dist/legacy/build/pdf.worker.mjs',
    import.meta.url,
  ).toString();
  const data = new Uint8Array(await file.arrayBuffer());
  const pdf = await pdfjs.getDocument({ data, useSystemFonts: true }).promise;
  const pages: string[] = [];
  const count = Math.min(pdf.numPages, 30);
  for (let i = 1; i <= count; i += 1) {
    const page = await pdf.getPage(i);
    const content = await page.getTextContent();
    pages.push(content.items.map((item: unknown) => (typeof item === 'object' && item !== null && 'str' in item ? String((item as { str: unknown }).str) : '')).join(' '));
  }
  await pdf.destroy();
  return pages.join('\n\n');
}

/** Release the microphone. A stopped recorder does not free the device on its own. */
function stopTracks(recorder: MediaRecorder | null) {
  recorder?.stream.getTracks().forEach((track) => track.stop());
}
