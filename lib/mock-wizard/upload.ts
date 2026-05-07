/**
 * Stable, wizard-scoped re-exports for chunked file uploads. The wizard never
 * touches the chunked-upload protocol directly — this module is the single
 * surface so future swaps (e.g. a different upload helper or progress hook)
 * stay isolated to one place.
 */

export {
  uploadFileChunked,
  type ChunkedUploadCommitResult,
  type PaperAssetRole,
} from '@/lib/content-upload-api';
