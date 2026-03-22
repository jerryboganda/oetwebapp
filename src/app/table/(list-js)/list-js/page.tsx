import React from "react";
import ListJsPage from "@/app/table/(list-js)/_components/ListJsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "List.js Tables - PolytronX",
    description:
      "Explore List.js table components for interactive and searchable data display in your React application.",
    keywords: [
      "list.js tables",
      "react tables",
      "UI tables",
      "table components",
      "data display",
      "table customization",
      "react optimization",
      "table performance",
      "table design",
      "table integration",
      "table workspace",
      "table components",
      "data grid",
      "list.js",
      "interactive tables",
      "searchable tables",
    ],
    openGraph: {
      title: "List.js Tables - PolytronX",
      description:
        "Explore List.js table components for interactive and searchable data display in your React application.",
      url: "/table/list-js",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ListJsPage />
    </div>
  );
};

export default Page;
