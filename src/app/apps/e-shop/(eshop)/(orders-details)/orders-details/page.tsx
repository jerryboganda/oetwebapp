import React from "react";
import OrdersDetailsPage from "@/app/apps/e-shop/(eshop)/(orders-details)/_components/OrdersDetailsPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Order Details - PolytronX",
    description:
      "View detailed information about your order including items, shipping, and payment details.",
    keywords: [
      "order details",
      "order information",
      "order tracking",
      "order status",
      "order items",
      "order shipping",
      "order payment",
      "order timeline",
      "order history",
      "order modifications",
      "order notes",
      "order cancellation",
      "order returns",
      "order refunds",
      "order analytics",
    ],
    openGraph: {
      title: "Order Details - PolytronX",
      description:
        "View detailed information about your order including items, shipping, and payment details.",
      url: "/apps/e-shop/orders-details",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <OrdersDetailsPage />
    </div>
  );
};

export default Page;
