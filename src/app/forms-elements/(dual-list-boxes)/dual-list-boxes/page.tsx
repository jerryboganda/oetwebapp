import React from "react";
import DualListBoxesPage from "@/app/forms-elements/(dual-list-boxes)/_components/DualListBoxesPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Dual List Boxes Forms - PolytronX",
    description:
      "Explore dual list box components for managing and transferring items between lists in your React forms.",
    keywords: [
      "dual list boxes",
      "list transfer",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "list box",
      "dual selection",
      "form customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "list management",
      "item transfer",
    ],
    openGraph: {
      title: "Dual List Boxes Forms - PolytronX",
      description:
        "Explore dual list box components for managing and transferring items between lists in your React forms.",
      url: "/forms-elements/dual-list-boxes",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <DualListBoxesPage />
    </div>
  );
};

export default Page;
