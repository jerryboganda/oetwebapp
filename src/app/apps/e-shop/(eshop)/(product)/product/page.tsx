import React from "react";
import ProductPage from "@/app/apps/e-shop/(eshop)/(product)/_components/ProductPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Product - PolytronX",
    description:
      "View and purchase individual products with detailed information and specifications.",
    keywords: [
      "product",
      "product view",
      "product purchase",
      "product details",
      "product specifications",
      "product pricing",
      "product variants",
      "product images",
      "product stock",
      "product reviews",
      "product ratings",
      "product comparison",
      "product features",
      "product availability",
      "product categories",
    ],
    openGraph: {
      title: "Product - PolytronX",
      description:
        "View and purchase individual products with detailed information and specifications.",
      url: "/apps/e-shop/product",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ProductPage />
    </div>
  );
};

export default Page;
