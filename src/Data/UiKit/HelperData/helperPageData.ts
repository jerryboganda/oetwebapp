export const textColors = [
  { class: "text-primary", label: "text-primary" },
  { class: "text-secondary", label: "text-secondary" },
  { class: "text-success", label: "text-success" },
  { class: "text-danger", label: "text-danger" },
  { class: "text-warning", label: "text-warning" },
  { class: "text-info", label: "text-info" },
  { class: "text-light", label: "text-light" },
  { class: "text-dark", label: "text-dark" },
];

export const linkColors = [
  { class: "link-primary", label: "link-primary" },
  { class: "link-secondary", label: "link-secondary" },
  { class: "link-success", label: "link-success" },
  { class: "link-danger", label: "link-danger" },
  { class: "link-warning", label: "link-warning" },
  { class: "link-info", label: "link-info" },
  { class: "link-light", label: "link-light" },
  { class: "link-dark", label: "link-dark" },
];

export const textBackgrounds = [
  { class: "txt-bg-primary", label: "txt-bg-primary" },
  { class: "txt-bg-secondary", label: "txt-bg-secondary" },
  { class: "txt-bg-success", label: "txt-bg-success" },
  { class: "txt-bg-danger", label: "txt-bg-danger" },
  { class: "txt-bg-warning", label: "txt-bg-warning" },
  { class: "txt-bg-info", label: "txt-bg-info" },
  { class: "txt-bg-light", label: "txt-bg-light" },
  { class: "txt-bg-dark", label: "txt-bg-dark" },
];

export const typographyStyles = [
  {
    title: "Text Align",
    description: "using text-* class for text align",
    items: [
      { class: "text-lowercase", label: "Text-Lowercase" },
      { class: "text-uppercase", label: "Text-Uppercase" },
      { class: "text-capitalize", label: "Text-Capitalize" },
      { class: "text-center", label: "Text Align Center" },
      { class: "text-start", label: "Text Align Start" },
      { class: "text-end", label: "Text Align End" },
    ],
  },
  {
    title: "Text Decoration",
    description: "using text-d-* class for text decoration",
    items: [
      { class: "text-decoration-underline", label: "underline" },
      { class: "text-decoration-line-through", label: "line-through" },
      { class: "text-decoration-overline", label: "overline" },
    ],
  },
  {
    title: "Font Style",
    description: "using f-fs-* class for font style",
    items: [
      { class: "fst-normal", label: "Normal" },
      { class: "fst-italic", label: "Italic" },
      { class: "fst-oblique", label: "Oblique" },
      { class: "fst-initial", label: "Initial" },
      { class: "fst-inherit", label: "Inherit" },
    ],
  },
  {
    title: "Headings",
    description: "using h1 to h6 class for Headings",
    items: [
      { class: "h1", label: "h1" },
      { class: "h2", label: "h2" },
      { class: "h3", label: "h3" },
      { class: "h4", label: "h4" },
      { class: "h5", label: "h5" },
      { class: "h6", label: "h6" },
    ],
  },
  {
    title: "Font Weight",
    description: "using f-fw-* class for font weight",
    items: [
      { class: "f-fw-100", label: "(100)" },
      { class: "f-fw-200", label: "(200)" },
      { class: "f-fw-300", label: "(300)" },
      { class: "f-fw-400", label: "(400)" },
      { class: "f-fw-500", label: "(500)" },
      { class: "f-fw-600", label: "(600)" },
      { class: "f-fw-700", label: "(700)" },
      { class: "f-fw-800", label: "(800)" },
      { class: "f-fw-900", label: "(900)" },
    ],
  },
  {
    title: "Font Size",
    description: "using f-s-* class for font size",
    items: [
      { class: "fs-1", label: "Size-1" },
      { class: "fs-2", label: "Size-2" },
      { class: "fs-3", label: "Size-3" },
      { class: "fs-4", label: "Size-4" },
      { class: "fs-5", label: "Size-5" },
      { class: "fs-6", label: "Size-6" },
    ],
  },
];

export const paddingSizes = [
  { class: "pa-2", label: "Padding-2" },
  { class: "pa-4", label: "Padding-4" },
  { class: "pa-8", label: "Padding-8" },
  { class: "pa-10", label: "Padding-10" },
  { class: "pa-14", label: "Padding-14" },
  { class: "pa-16", label: "Padding-16" },
];

export const paddingList = Array.from({ length: 40 }, (_, i) => ({
  class: `pa-${i + 1}`,
  label: `pa-${i + 1}`,
}));

export const sidePaddingList = [
  { type: "Top", prefix: "pa-t" },
  { type: "Start", prefix: "pa-s" },
  { type: "End", prefix: "pa-e" },
  { type: "Bottom", prefix: "pa-b" },
].map((side) => ({
  type: side.type,
  items: [4, 8, 10, 14, 18, 20, 24, 28, 30, 34, 38, 40].map((size) => ({
    class: `${side.prefix}-${size}`,
    label: `${side.type} ${size}`,
  })),
}));

export const marginSizes = [
  { class: "mg-40", label: "Margin-40" },
  { class: "mg-34", label: "Margin-34" },
  { class: "mg-30", label: "Margin-30" },
  { class: "mg-28", label: "Margin-28" },
  { class: "mg-24", label: "Margin-24" },
  { class: "mg-20", label: "Margin-20" },
  { class: "mg-14", label: "Margin-14" },
  { class: "mg-10", label: "Margin-10" },
  { class: "mg-4", label: "Margin-4" },
];

export const marginList = Array.from({ length: 40 }, (_, i) => ({
  class: `mg-${i + 1}`,
  label: `mg-${i + 1}`,
}));

export const sideMarginList = ["t", "s", "e", "b"].map((side) => ({
  type: side.toUpperCase(),
  items: [4, 8, 10, 14, 18, 20, 24, 28, 30, 34, 38, 40].map((size) => ({
    class: `mg-${side}-${size}`,
    label: `mg-${side.toUpperCase()}-${size}`,
  })),
}));

export const widthHeightData = [
  { width: 200, height: 200 },
  { width: 150, height: 150 },
  { width: 110, height: 110 },
  { width: 90, height: 90 },
  { width: 80, height: 80 },
  { width: 60, height: 60 },
  { width: 50, height: 50 },
];

export const borderStyles = [
  "border",
  "border-t",
  "border-s",
  "border-e",
  "border-b",
];

export const borderSideStyles = [
  "border-0",
  "border-t-0",
  "border-s-0",
  "border-e-0",
  "border-b-0",
];

export const borderColors = [
  "b-1-primary",
  "b-1-secondary",
  "b-1-success",
  "b-1-danger",
  "b-1-warning",
  "b-1-info",
  "b-1-light",
  "b-1-dark",
];

export const borderWidths = [
  "b-1-primary",
  "b-3-primary",
  "b-5-primary",
  "b-7-primary",
  "b-9-primary",
  "b-11-primary",
  "b-13-primary",
  "b-15-primary",
  "b-16-primary",
];

export const borderRadius = [
  "b-r-5",
  "b-r-10",
  "b-r-15",
  "b-r-20",
  "b-r-25",
  "b-r-30",
];

export const dashedBorders = [
  "dashed-1-primary",
  "dashed-2-secondary",
  "dashed-3-success",
  "dashed-4-warning",
  "dashed-5-danger",
  "dashed-6-dark",
  "dashed-8-info",
];

export const dottedBorders = [
  "dotted-1-primary",
  "dotted-2-secondary",
  "dotted-3-success",
  "dotted-4-warning",
  "dotted-5-danger",
  "dotted-6-dark",
  "dotted-8-info",
];

export const imageClasses = [
  "rounded",
  "rounded-top",
  "rounded-end",
  "rounded-bottom",
  "rounded-start",
  "rounded-pill",
  "rounded-circle w-120 h-120",
];

export const rotateData = [
  { degree: "90°", className: "rotate" },
  { degree: "180°", className: "rotate rotate-180" },
  { degree: "270°", className: "rotate rotate-270" },
  { degree: "-90°", className: "rotate rotate-90-1" },
  { degree: "-180°", className: "rotate rotate-180-1" },
  { degree: "-280°", className: "rotate rotate-280-1" },
];
