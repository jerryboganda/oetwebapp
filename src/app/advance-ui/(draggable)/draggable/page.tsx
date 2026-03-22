import React from "react";
import DraggablePage from "@/app/advance-ui/(draggable)/_components/DraggablePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Draggable - PolytronX",
    description:
      "Explore advanced draggable components and drag-and-drop functionality for your web applications.",
    keywords: [
      "draggable",
      "drag and drop",
      "react draggable",
      "drag components",
      "UI dragging",
      "drag functionality",
      "react drag",
      "drag events",
      "drag customization",
      "drag effects",
      "drag integration",
      "react drag and drop",
      "draggable elements",
      "drag handling",
      "drag library",
    ],
    openGraph: {
      title: "Draggable - PolytronX",
      description:
        "Explore advanced draggable components and drag-and-drop functionality for your web applications.",
      url: "/advance-ui/draggable",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <DraggablePage />
    </div>
  );
};

export default Page;
