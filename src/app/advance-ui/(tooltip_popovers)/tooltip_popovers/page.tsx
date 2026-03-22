import React from "react";
import TooltipPopoverPage from "@/app/advance-ui/(tooltip_popovers)/_components/TooltipPopoverPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Tooltip & Popovers - PolytronX",
    description:
      "Explore advanced tooltip and popover components for your web applications.",
    keywords: [
      "tooltip",
      "popover",
      "react tooltip",
      "react popover",
      "UI tooltip",
      "tooltip component",
      "popover component",
      "tooltip customization",
      "popover customization",
      "tooltip styles",
      "popover styles",
      "tooltip effects",
      "popover effects",
      "tooltip integration",
      "popover integration",
      "tooltip library",
      "popover library",
      "react tooltip",
      "react popover",
      "tooltip optimization",
      "popover optimization",
    ],
    openGraph: {
      title: "Tooltip & Popovers - PolytronX",
      description:
        "Explore advanced tooltip and popover components for your web applications.",
      url: "/advance-ui/tooltip-popovers",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TooltipPopoverPage />
    </div>
  );
};

export default Page;
