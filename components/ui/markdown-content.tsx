import type { CSSProperties, ReactNode } from 'react';
import { cn } from '@/lib/utils';

export interface MarkdownContentProps {
  markdown: string;
  className?: string;
}

type TableAlignment = 'left' | 'center' | 'right';

type MarkdownBlock =
  | { type: 'heading'; level: 1 | 2 | 3 | 4 | 5 | 6; text: string }
  | { type: 'paragraph'; text: string }
  | { type: 'blockquote'; text: string }
  | { type: 'list'; ordered: boolean; items: string[] }
  | { type: 'code'; text: string }
  | { type: 'table'; header: string[]; alignments: TableAlignment[]; rows: string[][] };

export function MarkdownContent({ markdown, className }: MarkdownContentProps) {
  const blocks = parseMarkdownBlocks(markdown ?? '');
  if (blocks.length === 0) return null;

  return <div className={cn('space-y-3', className)}>{blocks.map((block, index) => renderBlock(block, index))}</div>;
}

function renderBlock(block: MarkdownBlock, index: number): ReactNode {
  const key = `${block.type}-${index}`;

  switch (block.type) {
    case 'heading': {
      const headingClasses = 'font-bold tracking-tight text-current';
      switch (block.level) {
        case 1:
          return <h1 key={key} className={cn(headingClasses, 'text-xl')}>{renderInline(block.text, `${key}-inline`)}</h1>;
        case 2:
          return <h2 key={key} className={cn(headingClasses, 'text-lg')}>{renderInline(block.text, `${key}-inline`)}</h2>;
        case 3:
          return <h3 key={key} className={cn(headingClasses, 'text-base')}>{renderInline(block.text, `${key}-inline`)}</h3>;
        case 4:
          return <h4 key={key} className={cn(headingClasses, 'text-sm')}>{renderInline(block.text, `${key}-inline`)}</h4>;
        case 5:
          return <h5 key={key} className={cn(headingClasses, 'text-sm')}>{renderInline(block.text, `${key}-inline`)}</h5>;
        case 6:
        default:
          return <h6 key={key} className={cn(headingClasses, 'text-xs uppercase tracking-[0.14em]')}>{renderInline(block.text, `${key}-inline`)}</h6>;
      }
    }
    case 'paragraph':
      return (
        <p key={key} className="whitespace-pre-wrap leading-6 text-current">
          {renderInline(block.text, `${key}-inline`)}
        </p>
      );
    case 'blockquote':
      return (
        <blockquote key={key} className="rounded-xl border border-border bg-background-light px-4 py-3 italic text-current">
          <span className="whitespace-pre-wrap leading-6">
            {renderInline(block.text, `${key}-inline`)}
          </span>
        </blockquote>
      );
    case 'list':
      return block.ordered ? (
        <ol key={key} className="list-decimal space-y-1 pl-5 leading-6 text-current">
          {block.items.map((item, itemIndex) => (
            <li key={`${key}-item-${itemIndex}`} className="whitespace-pre-wrap">
              {renderInline(item, `${key}-item-${itemIndex}`)}
            </li>
          ))}
        </ol>
      ) : (
        <ul key={key} className="list-disc space-y-1 pl-5 leading-6 text-current">
          {block.items.map((item, itemIndex) => (
            <li key={`${key}-item-${itemIndex}`} className="whitespace-pre-wrap">
              {renderInline(item, `${key}-item-${itemIndex}`)}
            </li>
          ))}
        </ul>
      );
    case 'code':
      return (
        <pre key={key} className="overflow-x-auto rounded-xl border border-border bg-background-light px-4 py-3 text-xs leading-6 text-current">
          <code className="font-mono whitespace-pre-wrap">{block.text}</code>
        </pre>
      );
    case 'table':
      return (
        <div key={key} className="overflow-x-auto rounded-xl border border-border">
          <table className="min-w-full border-collapse text-sm text-current">
            <thead className="bg-background-light">
              <tr className="border-b border-border/60">
                {block.header.map((cell, cellIndex) => {
                  const alignment = block.alignments[cellIndex] ?? 'left';
                  return (
                    <th
                      key={`${key}-head-${cellIndex}`}
                      scope="col"
                      className="border-b border-border/60 px-3 py-2 text-left font-semibold"
                      style={tableAlignmentStyle(alignment)}
                    >
                      {renderInline(cell, `${key}-head-${cellIndex}`)}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody className="divide-y divide-border/50 bg-surface">
              {block.rows.map((row, rowIndex) => (
                <tr key={`${key}-row-${rowIndex}`}>
                  {block.header.map((_, cellIndex) => {
                    const alignment = block.alignments[cellIndex] ?? 'left';
                    const cell = row[cellIndex] ?? '';
                    return (
                      <td
                        key={`${key}-row-${rowIndex}-cell-${cellIndex}`}
                        className="px-3 py-2 align-top"
                        style={tableAlignmentStyle(alignment)}
                      >
                        <div className="whitespace-pre-wrap leading-6">
                          {renderInline(cell, `${key}-row-${rowIndex}-cell-${cellIndex}`)}
                        </div>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    default:
      return null;
  }
}

function tableAlignmentStyle(alignment: TableAlignment): CSSProperties {
  return {
    textAlign: alignment,
  };
}

function parseMarkdownBlocks(markdown: string): MarkdownBlock[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const blocks: MarkdownBlock[] = [];
  let index = 0;

  while (index < lines.length) {
    const line = lines[index];
    if (!line.trim()) {
      index += 1;
      continue;
    }

    const heading = line.match(/^(#{1,6})\s+(.+)$/);
    if (heading) {
      blocks.push({
        type: 'heading',
        level: heading[1].length as 1 | 2 | 3 | 4 | 5 | 6,
        text: heading[2].trim(),
      });
      index += 1;
      continue;
    }

    if (line.startsWith('```')) {
      const fenceInfo = line.slice(3).trim();
      const codeLines: string[] = [];
      index += 1;
      while (index < lines.length && !lines[index].startsWith('```')) {
        codeLines.push(lines[index]);
        index += 1;
      }
      if (index < lines.length && lines[index].startsWith('```')) {
        index += 1;
      }
      blocks.push({ type: 'code', text: codeLines.join('\n') });
      void fenceInfo;
      continue;
    }

    if (isTableStart(lines, index)) {
      const table = parseTableBlock(lines, index);
      if (table) {
        blocks.push(table.block);
        index = table.nextIndex;
        continue;
      }
    }

    if (line.trimStart().startsWith('> ')) {
      const quoteLines: string[] = [];
      while (index < lines.length) {
        const current = lines[index];
        if (!current.trim()) break;
        if (!current.trimStart().startsWith('> ')) break;
        quoteLines.push(current.trimStart().replace(/^>\s?/, ''));
        index += 1;
      }
      blocks.push({ type: 'blockquote', text: quoteLines.join('\n') });
      continue;
    }

    const listMatch = line.match(/^(\s*)(?:[-*+]|\d+\.)\s+(.+)$/);
    if (listMatch) {
      const ordered = /\d+\.$/.test(listMatch[0].trimStart().split(/\s+/)[0] ?? '');
      const items: string[] = [];
      while (index < lines.length) {
        const current = lines[index];
        const currentMatch = current.match(/^(\s*)(?:[-*+]|\d+\.)\s+(.+)$/);
        if (!currentMatch) break;
        items.push(currentMatch[2]);
        index += 1;
      }
      blocks.push({ type: 'list', ordered, items });
      continue;
    }

    const paragraphLines: string[] = [];
    while (index < lines.length) {
      const current = lines[index];
      if (!current.trim()) break;
      if (paragraphLines.length > 0 && isBlockStarter(current, lines[index + 1])) break;
      if (paragraphLines.length === 0 && isBlockStarter(current, lines[index + 1])) break;
      paragraphLines.push(current);
      index += 1;
    }
    if (paragraphLines.length === 0) {
      paragraphLines.push(line);
      index += 1;
    }
    blocks.push({ type: 'paragraph', text: paragraphLines.join('\n') });
  }

  return blocks;
}

function isBlockStarter(line: string, nextLine?: string): boolean {
  if (!line.trim()) return true;
  if (/^#{1,6}\s+/.test(line)) return true;
  if (line.startsWith('```')) return true;
  if (line.trimStart().startsWith('> ')) return true;
  if (/^(\s*)(?:[-*+]|\d+\.)\s+/.test(line)) return true;
  return isTableStart([line, nextLine ?? ''], 0);
}

function isTableStart(lines: string[], index: number): boolean {
  const current = lines[index];
  const next = lines[index + 1];
  if (!current || !next) return false;
  if (!current.includes('|')) return false;
  return isTableSeparator(next);
}

function isTableSeparator(line: string): boolean {
  const cells = splitTableRow(line);
  if (cells.length < 2) return false;
  return cells.every((cell) => /^:?-{3,}:?$/.test(cell.replace(/\s+/g, '')));
}

function parseTableBlock(lines: string[], startIndex: number): { block: MarkdownBlock; nextIndex: number } | null {
  if (!isTableStart(lines, startIndex)) return null;

  const header = splitTableRow(lines[startIndex]);
  const separator = splitTableRow(lines[startIndex + 1]);
  const alignments = separator.map(parseTableAlignment);
  const rows: string[][] = [];
  let index = startIndex + 2;

  while (index < lines.length) {
    const current = lines[index];
    if (!current.trim()) break;
    if (!current.includes('|')) break;
    rows.push(splitTableRow(current));
    index += 1;
  }

  return {
    block: {
      type: 'table',
      header,
      alignments: alignments.length > 0 ? alignments : header.map(() => 'left'),
      rows,
    },
    nextIndex: index,
  };
}

function splitTableRow(row: string): string[] {
  const trimmed = row.trim();
  const content = trimmed.startsWith('|') ? trimmed.slice(1) : trimmed;
  const normalized = content.endsWith('|') ? content.slice(0, -1) : content;
  const cells: string[] = [];
  let current = '';
  let escaped = false;

  for (const char of normalized) {
    if (escaped) {
      current += char;
      escaped = false;
      continue;
    }
    if (char === '\\') {
      escaped = true;
      continue;
    }
    if (char === '|') {
      cells.push(current.trim());
      current = '';
      continue;
    }
    current += char;
  }

  cells.push(current.trim());
  return cells;
}

function parseTableAlignment(cell: string): TableAlignment {
  const value = cell.trim();
  const left = value.startsWith(':');
  const right = value.endsWith(':');
  if (left && right) return 'center';
  if (right) return 'right';
  return 'left';
}

function renderInline(text: string, keyPrefix: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  let index = 0;
  let keyIndex = 0;

  const pushText = (value: string) => {
    if (value) nodes.push(value);
  };

  while (index < text.length) {
    if (text.startsWith('**', index)) {
      const end = text.indexOf('**', index + 2);
      if (end !== -1) {
        const inner = text.slice(index + 2, end);
        nodes.push(
          <strong key={`${keyPrefix}-strong-${keyIndex++}`}>
            {renderInline(inner, `${keyPrefix}-strong-${keyIndex}`)}
          </strong>,
        );
        index = end + 2;
        continue;
      }
    }

    if (text.startsWith('__', index)) {
      const end = text.indexOf('__', index + 2);
      if (end !== -1) {
        const inner = text.slice(index + 2, end);
        nodes.push(
          <strong key={`${keyPrefix}-strong-${keyIndex++}`}>
            {renderInline(inner, `${keyPrefix}-strong-${keyIndex}`)}
          </strong>,
        );
        index = end + 2;
        continue;
      }
    }

    if (text[index] === '[') {
      const link = parseLink(text.slice(index));
      if (link) {
        const safeHref = sanitizeHref(link.href);
        if (safeHref) {
          nodes.push(
            <a key={`${keyPrefix}-link-${keyIndex++}`} href={safeHref} className="underline underline-offset-2">
              {renderInline(link.label, `${keyPrefix}-link-${keyIndex}`)}
            </a>,
          );
        } else {
          pushText(`[${link.label}](${link.href})`);
        }
        index += link.length;
        continue;
      }
    }

    if (text[index] === '`') {
      const end = text.indexOf('`', index + 1);
      if (end !== -1) {
        nodes.push(
          <code key={`${keyPrefix}-code-${keyIndex++}`} className="rounded border border-border bg-background-light px-1 py-0.5 font-mono text-[0.9em] text-current">
            {text.slice(index + 1, end)}
          </code>,
        );
        index = end + 1;
        continue;
      }
    }

    if (text[index] === '*' && text[index + 1] !== '*') {
      const end = text.indexOf('*', index + 1);
      if (end !== -1) {
        const inner = text.slice(index + 1, end);
        nodes.push(
          <em key={`${keyPrefix}-em-${keyIndex++}`}>
            {renderInline(inner, `${keyPrefix}-em-${keyIndex}`)}
          </em>,
        );
        index = end + 1;
        continue;
      }
    }

    let nextIndex = index + 1;
    while (nextIndex < text.length && !isInlineSpecialStart(text, nextIndex)) {
      nextIndex += 1;
    }
    pushText(text.slice(index, nextIndex));
    index = nextIndex;
  }

  return nodes;
}

function isInlineSpecialStart(text: string, index: number): boolean {
  return text[index] === '*' || text[index] === '_' || text[index] === '`' || text[index] === '[';
}

function parseLink(text: string): { label: string; href: string; length: number } | null {
  const match = text.match(/^\[([^\]]+)\]\(([^)]+)\)/);
  if (!match) return null;
  return {
    label: match[1],
    href: match[2],
    length: match[0].length,
  };
}

function sanitizeHref(href: string): string | null {
  const trimmed = href.trim();
  if (/^(https?:|mailto:|tel:|\/|#)/i.test(trimmed)) return trimmed;
  return null;
}
