import React from "react";
import DataTablePage from "@/app/table/(data-table)/_components/DataTablePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Data Tables - PolytronX",
    description:
      "Explore advanced data table components with sorting, filtering, and pagination in your React application.",
    keywords: [
      "data tables",
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
      "table features",
      "sorting",
      "filtering",
      "pagination",
    ],
    openGraph: {
      title: "Data Tables - PolytronX",
      description:
        "Explore advanced data table components with sorting, filtering, and pagination in your React application.",
      url: "/table/data-table",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <DataTablePage />
    </div>
  );
};

export default Page;
