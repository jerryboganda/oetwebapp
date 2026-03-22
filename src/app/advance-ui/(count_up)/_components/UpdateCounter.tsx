import React, { useEffect, useRef, ElementType } from "react";
import { CountUp } from "countup.js";
import {
  IconApps,
  IconArrowNarrowDown,
  IconArrowNarrowUp,
  IconBrandPaypal,
  IconReportAnalytics,
} from "@tabler/icons-react";

interface CounterProps {
  value: number;
  className?: string;
  tag?: ElementType;
}

const Counter: React.FC<CounterProps> = ({
  value,
  className,
  tag: Tag = "div",
}) => {
  const counterRef = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (counterRef.current) {
      const countUp = new CountUp(counterRef.current, value, {
        duration: 2,
        separator: ",",
      });
      countUp.start();
    }
  }, [value]);

  return <Tag ref={counterRef} className={className} />;
};

export default Counter;
export interface CounterItem {
  icon: JSX.Element;
  iconClass: string;
  value: number;
  prefix?: string;
  suffix?: string;
  description: string;
  tag: "div" | "p";
}

export const counterItems: CounterItem[] = [
  {
    icon: <IconApps size={28} />,
    iconClass: "ti ti-apps",
    value: 500,
    prefix: "$",
    description: "Respected Companies",
    tag: "p",
  },
  {
    icon: <IconReportAnalytics size={28} />,
    iconClass: "ti ti-report-analytics",
    value: 75,
    description: "Analytical Reports",
    tag: "div",
  },
  {
    icon: <IconBrandPaypal size={28} />,
    iconClass: "ti ti-brand-paypal",
    value: 40,
    suffix: "%",
    description: "Protected Payments",
    tag: "p",
  },
];

export const tabData = [
  {
    id: "1",
    items: [
      {
        value: 150,
        label: "Income",
        icon: <IconArrowNarrowUp className="text-success" />,
        prefix: "$",
      },
      {
        value: 85,
        label: "Projects",
        icon: <IconArrowNarrowDown className="text-danger" />,
        suffix: "%",
      },
      {
        value: 150,
        label: "Achievement",
        icon: <IconArrowNarrowUp className="text-success" />,
        suffix: "%",
      },
    ],
  },
  {
    id: "2",
    items: [
      {
        value: 110,
        label: "Income",
        icon: <IconArrowNarrowUp className="text-success" />,
        prefix: "$",
      },
      {
        value: 65,
        label: "Projects",
        icon: <IconArrowNarrowDown className="text-danger" />,
        suffix: "%",
      },
      {
        value: 3200,
        label: "Achievement",
        icon: <IconArrowNarrowUp className="text-success" />,
        suffix: "%",
      },
    ],
  },
  {
    id: "3",
    items: [
      {
        value: 100,
        label: "Income",
        icon: <IconArrowNarrowUp className="text-success" />,
        prefix: "$",
      },
      {
        value: 70,
        label: "Projects",
        icon: <IconArrowNarrowDown className="text-danger" />,
        suffix: "%",
      },
      {
        value: 1200,
        label: "Achievement",
        icon: <IconArrowNarrowUp className="text-success" />,
        suffix: "%",
      },
    ],
  },
];
export const simpleCounterItems = [
  {
    prefix: "$",
    value: 150,
    suffix: "",
    icon: <IconArrowNarrowUp size={24} className="text-success" />,
    label: "Income",
  },
  {
    prefix: "",
    value: 85,
    suffix: "",
    icon: <IconArrowNarrowDown size={24} className="text-danger" />,
    label: "Projects",
  },
  {
    prefix: "",
    value: 60,
    suffix: "%",
    icon: <IconArrowNarrowUp size={24} className="text-success" />,
    label: "Achievement",
  },
];
export interface UpdateCounterItem {
  value: number;
  prefix?: string;
  suffix?: string;
  iconClass: string;
  iconColorClass: string;
  label: string;
}

export const updateCounterItems: UpdateCounterItem[] = [
  {
    value: 200,
    prefix: "$",
    iconClass: "ti ti-arrow-narrow-up",
    iconColorClass: "text-success",
    label: "Income",
  },
  {
    value: 81,
    iconClass: "ti ti-arrow-narrow-down",
    iconColorClass: "text-danger",
    label: "Projects",
  },
  {
    value: 60,
    suffix: "%",
    iconClass: "ti ti-arrow-narrow-up",
    iconColorClass: "text-success",
    label: "Achievement",
  },
];
