import React from "react";
import ProjectdetailPage from "@/app/apps/projects-page/(projects-details)/_components/ProjectdetailPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Project Details - PolytronX",
    description:
      "Explore advanced project details and task management features for your web applications.",
    keywords: [
      "project details",
      "task management",
      "react project",
      "UI project",
      "project component",
      "project tracking",
      "project customization",
      "project styles",
      "project effects",
      "project integration",
      "project library",
      "react project app",
      "project optimization",
      "project performance",
      "task details",
    ],
    openGraph: {
      title: "Project Details - PolytronX",
      description:
        "Explore advanced project details and task management features for your web applications.",
      url: "/apps/projects/details",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ProjectdetailPage />
    </div>
  );
};

export default Page;
