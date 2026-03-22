import React from "react";
import ReadyTouseTablePage from "@/app/ready-to-use/(ready-to-use-tables)/_components/ReadyTouseTablePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Ready To Use Tables - PolytronX",
    description:
      "Explore prebuilt tables like Patients, Students, Payments, Jobs, and Tickets.",
    keywords: [
      "admin dashboard",
      "data tables",
      "React Bootstrap",
      "PolytronX",
      "responsive tables",
      "user management",
      "job listings",
      "ticketing system",
    ],
    openGraph: {
      title: "Ready To Use Tables - PolytronX",
      description:
        "Prebuilt admin tables for managing patients, students, jobs, and more.",
      url: "/ready-to-use/ready-to-use-tables",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ReadyTouseTablePage />
    </div>
  );
};

export default Page;
