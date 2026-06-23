/**
 * TipTap schema pieces for the Part A note-completion WYSIWYG editor.
 *
 *  - `PartAGap`: an inline ATOM node that represents one fill-in-the-blank.
 *    Because it is an atom it is a single indivisible token: the author can put
 *    text before AND after it on the same line ("bloating, ____ and fatigue"),
 *    it can never be split or half-typed, and — crucially — typing "(1)" as
 *    text can never accidentally become a gap. This is the direct fix for the
 *    "0 gaps" data-entry bug. It serialises to the canonical `____` marker via
 *    `tiptapDocToGrammar`.
 *
 *  - `PartAListAttribute`: a global attribute that tags paragraphs with their
 *    note layout level (0 = context line, 1 = bullet, 2 = sub-bullet) so the
 *    flat 2-level OET note grammar round-trips losslessly without native nested
 *    lists. Rendered with a `data-part-a-list` attribute + class that globals.css
 *    styles into bullets/indentation.
 */
import { Node, Extension, mergeAttributes } from '@tiptap/core';

export const PART_A_GAP_NODE_NAME = 'partAGap';

export const PartAGap = Node.create({
  name: PART_A_GAP_NODE_NAME,
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  draggable: false,

  parseHTML() {
    return [{ tag: 'span[data-part-a-gap]' }];
  },

  renderHTML({ HTMLAttributes }) {
    return [
      'span',
      mergeAttributes(HTMLAttributes, {
        'data-part-a-gap': 'true',
        class: 'part-a-gap',
        contenteditable: 'false',
      }),
      '____',
    ];
  },
});

export const PartAListAttribute = Extension.create({
  name: 'partAListAttribute',

  addGlobalAttributes() {
    return [
      {
        types: ['paragraph'],
        attributes: {
          partAList: {
            default: 0,
            parseHTML: (element) => {
              const raw = element.getAttribute('data-part-a-list');
              const n = raw ? Number(raw) : 0;
              return n === 1 || n === 2 ? n : 0;
            },
            renderHTML: (attributes) => {
              const level = attributes.partAList;
              if (level !== 1 && level !== 2) return {};
              return {
                'data-part-a-list': String(level),
                class: `part-a-list part-a-list-${level}`,
              };
            },
          },
        },
      },
    ];
  },
});
