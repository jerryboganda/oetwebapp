import React from "react";
import NotificationsPage from "@/app/ui-kit/(notifications)/_components/NotificationsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Notifications - PolytronX",
    description:
      "Explore notification components for user feedback and alerts in your React application.",
    keywords: [
      "notifications",
      "react components",
      "UI components",
      "user feedback",
      "alerts",
      "react notification",
      "UI notification",
      "component customization",
      "react optimization",
      "component performance",
      "notification design",
      "UI integration",
      "alert components",
      "feedback components",
    ],
    openGraph: {
      title: "Notifications - PolytronX",
      description:
        "Explore notification components for user feedback and alerts in your React application.",
      url: "/ui-kit/notifications",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <NotificationsPage />
    </div>
  );
};

export default Page;
