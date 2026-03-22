// accordionData.ts
export type AccordionItemType = {
  id: string;
  htmlId: string;
  title: string;
  content: string;
};

export const accordionItems: AccordionItemType[] = [
  {
    id: "1",
    htmlId: "collapseOne",
    title: "Accordion Item #1",
    content:
      "This is the first item's accordion body. It is shown by default, until the collapse plugin adds the appropriate classes that we use to style each element. These classes control the overall appearance, as well as the showing and hiding via CSS transitions. You can modify any of this with custom CSS or overriding our default variables. It's also worth noting that just about any HTML can go within the .accordion-body, though the transition does limit overflow.",
  },
  {
    id: "2",
    htmlId: "collapseTwo",
    title: "Accordion Item #2",
    content:
      "This is the second item's accordion body. It is hidden by default, until the collapse plugin adds the appropriate classes.",
  },
  {
    id: "3",
    htmlId: "collapseThree",
    title: "Accordion Item #3",
    content:
      "This is the third item's accordion body. It is hidden by default, until the collapse plugin adds the appropriate classes.",
  },
];

export const outlineAccordionItems = [
  {
    id: "1",
    title: "Accordion Item #1",
    content: `This is the first item's accordion body. It is shown by default, until the
    collapse plugin adds the appropriate classes that we use to style each element. These
    classes control the overall appearance, as well as the showing and hiding via CSS
    transitions. You can modify any of this with custom CSS or overriding our default variables.
    It's also worth noting that just about any HTML can go within the <code>.accordion-body</code>,
    though the transition does limit overflow.`,
  },
  {
    id: "2",
    title: "Accordion Item #2",
    content: `This is the second item's accordion body. It is hidden by default, until the
    collapse plugin adds the appropriate classes that we use to style each element. These
    classes control the overall appearance, as well as the showing and hiding via CSS
    transitions.`,
  },
  {
    id: "3",
    title: "Accordion Item #3",
    content: `This is the third item's accordion body. It is hidden by default, until the
    collapse plugin adds the appropriate classes. Any HTML can go within the <code>.accordion-body</code>.`,
  },
];

export const lightAccordionItems = [
  {
    id: "1",
    title: "Accordion Item #1",
    content: `Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush class</code>. This is the first item's accordion body.`,
  },
  {
    id: "2",
    title: "Accordion Item #2",
    content: `Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush class</code>. This is the first item's accordion body.`,
  },
  {
    id: "3",
    title: "Accordion Item #3",
    content: `Placeholder content for this accordion, which is intended to demonstrate the <code>.accordion-flush class</code>. This is the first item's accordion body.`,
  },
];
