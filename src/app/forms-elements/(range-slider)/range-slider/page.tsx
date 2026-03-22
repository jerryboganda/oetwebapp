import React from "react";
import RangeSliderPage from "@/app/forms-elements/(range-slider)/_components/RangeSliderPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Range Slider Forms - PolytronX",
    description:
      "Explore range slider components for selecting values in your React forms.",
    keywords: [
      "range slider",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "slider customization",
      "value selection",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "slider animation",
      "value range",
    ],
    openGraph: {
      title: "Range Slider Forms - PolytronX",
      description:
        "Explore range slider components for selecting values in your React forms.",
      url: "/forms-elements/range-slider",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <RangeSliderPage />
    </div>
  );
};

export default Page;
