import React from "react";
import OrderPage from "@/app/apps/e-shop/(eshop)/(orders)/_components/OrderPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Orders - PolytronX",
    description:
      "View and manage individual orders with detailed information and status updates.",
    keywords: [
      "orders",
      "order details",
      "order status",
      "order tracking",
      "order history",
      "order items",
      "order shipping",
      "order payment",
      "order timeline",
      "order cancellation",
      "order modification",
      "order notes",
      "order history",
      "order analytics",
      "order management",
    ],
    openGraph: {
      title: "Orders - PolytronX",
      description:
        "View and manage individual orders with detailed information and status updates.",
      url: "/apps/e-shop/orders",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <OrderPage />
    </div>
  );
};

export default Page;
