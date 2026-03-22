interface CardItem {
  id: string;
  title: string;
  subtitle?: string;
  bodyText: string;
  footer?: string;
  cardClass?: string;
  showHeader?: boolean;
  showFooter?: boolean;
}

export const cardItems: CardItem[] = [
  {
    id: "togglerCard1",
    title: "Card Header",
    subtitle: "Card body",
    bodyText:
      "With supporting text below as a natural lead-in to additional content.",
    showHeader: true,
  },
  {
    id: "togglerCard2",
    title: "Card Footer",
    subtitle: "Card Body",
    bodyText:
      "With supporting text below as a natural lead-in to additional content.",
    showFooter: true,
  },
  {
    id: "togglerCard3",
    title: "Card Header",
    bodyText:
      "With supporting text below as a natural lead-in to additional content below as a natural.",
    footer: "Card Footer",
    showHeader: true,
    showFooter: true,
    cardClass: "border-0",
  },
  {
    id: "togglerCard4",
    title: "Hover Effect",
    subtitle: "Card body",
    bodyText:
      "With supporting text below lead-in to additional content below as a natural.",
    showHeader: true,
    cardClass: "hover-effect",
  },
];
