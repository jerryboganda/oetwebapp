import React from "react";
import InvoicePage from "@/app/apps/(invoice)/_components/InvoicePage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Invoices - PolytronX",
    description:
      "Generate and manage professional invoices with our invoice management system.",
    keywords: [
      "invoices",
      "invoice management",
      "billing",
      "invoice generation",
      "invoice workspace",
      "invoice tracking",
      "invoice payment",
      "invoice history",
      "invoice customization",
      "invoice settings",
      "invoice export",
      "invoice import",
      "invoice status",
      "invoice categories",
      "invoice reporting",
    ],
    openGraph: {
      title: "Invoices - PolytronX",
      description:
        "Generate and manage professional invoices with our invoice management system.",
      url: "/apps/invoice",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <InvoicePage />
    </div>
  );
};

export default Page;
