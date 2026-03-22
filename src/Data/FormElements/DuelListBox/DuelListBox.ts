export const options = [
  { value: "1", label: "One" },
  { value: "2", label: "Two" },
  { value: "3", label: "Three" },
];
export const options2 = [
  { value: "1", label: "One" },
  { value: "2", label: "Two" },
  { value: "3", label: "Three" },
  { value: "4", label: "Four" },
  { value: "5", label: "Five" },
  { value: "6", label: "Six" },
  { value: "7", label: "Seven" },
  { value: "8", label: "Eight" },
  { value: "9", label: "Nine" },
];
export const allOptions = [
  [
    "enableDoubleClick",
    "true",
    "boolean",
    "If double clicking a list item should move it between lists.",
  ],
  ["showAddButton", "true", "boolean", 'If the "Add" button should be shown.'],
  [
    "showRemoveButton",
    "true",
    "boolean",
    'If the "Remove" button should be shown.',
  ],
  [
    "showAddAllButton",
    "true",
    "boolean",
    'If the "Add All" button should be shown.',
  ],
  [
    "showRemoveAllButton",
    "true",
    "boolean",
    'If the "Remove All" button should be shown.',
  ],
  [
    "availableTitle",
    '"Available options"',
    "string",
    "Title shown above the left (available) list.",
  ],
  [
    "selectedTitle",
    '"Selected options"',
    "string",
    "Title shown above the right (selected) list.",
  ],
  ["addButtonText", '"Add"', "string", 'Text displayed in the "Add" button.'],
  [
    "removeButtonText",
    '"Remove"',
    "string",
    'Text displayed in the "Remove" button.',
  ],
  [
    "addAllButtonText",
    '"Add All"',
    "string",
    'Text displayed in the "Add All" button.',
  ],
  [
    "removeAllButtonText",
    '"Remove All"',
    "string",
    'Text displayed in the "Remove All" button.',
  ],
  [
    "searchPlaceholder",
    '"Search..."',
    "string",
    "Placeholder text for search inputs.",
  ],
  [
    "options",
    "undefined",
    "string[]",
    "Array of option names used to populate the lists.",
  ],
];

export const publicFunctions = [
  ["addOption", "option", "Add a single option to the left list."],
  ["addOptions", "options", "Add multiple options to the left list."],
  [
    "changeSelected",
    "option",
    "Toggle the selected state of the given option.",
  ],
  ["actionAllSelected", "", "Select all items in the left list."],
  ["actionAllDeselected", "", "Deselect all items from the right list."],
  ["redraw", "", "Redraw the lists (re-renders component)."],
];
