/**
 * Verification-code digit normalization.
 *
 * JavaScript's `\d` (without the Unicode flag) matches ASCII `[0-9]` ONLY, so a
 * learner typing on an Arabic keyboard produces Arabic-Indic digits that every
 * `replace(/\D/g, '')` filter silently deletes — the OTP boxes stay empty while
 * the field is focused and the keyboard is open. Normalize to ASCII first, then
 * filter.
 *
 * Covered digit sets:
 *   ASCII                      0-9            U+0030..U+0039
 *   Arabic-Indic               ٠١٢٣٤٥٦٧٨٩     U+0660..U+0669
 *   Extended Arabic-Indic      ۰۱۲۳۴۵۶۷۸۹     U+06F0..U+06F9
 */
const ARABIC_INDIC_ZERO = 0x0660;
const EXTENDED_ARABIC_INDIC_ZERO = 0x06f0;

/** Map any supported digit character to its ASCII equivalent, or null. */
function asciiDigitFor(character: string): string | null {
  const code = character.codePointAt(0);
  if (code === undefined) return null;
  if (code >= 0x30 && code <= 0x39) return character;
  if (code >= ARABIC_INDIC_ZERO && code <= ARABIC_INDIC_ZERO + 9) {
    return String(code - ARABIC_INDIC_ZERO);
  }
  if (code >= EXTENDED_ARABIC_INDIC_ZERO && code <= EXTENDED_ARABIC_INDIC_ZERO + 9) {
    return String(code - EXTENDED_ARABIC_INDIC_ZERO);
  }
  return null;
}

/**
 * Keep only digits, normalized to ASCII 0-9. Everything else (spaces, dashes,
 * letters, RTL marks pasted alongside a code) is dropped.
 */
export function toAsciiDigits(input: string | null | undefined): string {
  if (!input) return '';
  let out = '';
  for (const character of input) {
    const digit = asciiDigitFor(character);
    if (digit !== null) out += digit;
  }
  return out;
}
