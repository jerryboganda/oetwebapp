import React from "react";
import TypeaheadPage from "@/app/forms-elements/(typeahead)/_components/TypeaheadPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Typeahead Forms - PolytronX",
    description:
      "Explore typeahead components for autocomplete functionality in your React forms.",
    keywords: [
      "typeahead",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "autocomplete",
      "suggestions",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "typeahead customization",
      "search suggestions",
    ],
    openGraph: {
      title: "Typeahead Forms - PolytronX",
      description:
        "Explore typeahead components for autocomplete functionality in your React forms.",
      url: "/forms-elements/typeahead",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TypeaheadPage />
    </div>
  );
};

export default Page;
