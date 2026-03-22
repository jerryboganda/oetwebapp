import React from "react";
import ButtonsPage from "@/app/ui-kit/(buttons)/_components/ButtonsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Buttons - PolytronX",
    description:
      "Explore button components with various styles and interactions for your React application.",
    keywords: [
      "buttons",
      "react components",
      "UI components",
      "interactive elements",
      "react button",
      "UI button",
      "component customization",
      "react optimization",
      "component performance",
      "button design",
      "UI integration",
      "action components",
      "interactive components",
    ],
    openGraph: {
      title: "Buttons - PolytronX",
      description:
        "Explore button components with various styles and interactions for your React application.",
      url: "/ui-kit/buttons",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ButtonsPage />
    </div>
  );
};

export default Page;
