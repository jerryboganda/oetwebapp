import React from "react";
import AddProductPage from "@/app/apps/e-shop/(eshop)/(add-product)/_components/AddProductPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Add Product - PolytronX",
    description:
      "Add new products to your store with our intuitive product management system.",
    keywords: [
      "add product",
      "product management",
      "product creation",
      "product details",
      "product pricing",
      "product categories",
      "product images",
      "product variants",
      "product inventory",
      "product attributes",
      "product SEO",
      "product tags",
      "product descriptions",
      "product specifications",
      "product settings",
    ],
    openGraph: {
      title: "Add Product - PolytronX",
      description:
        "Add new products to your store with our intuitive product management system.",
      url: "/apps/e-shop/add-product",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AddProductPage />
    </div>
  );
};

export default Page;
