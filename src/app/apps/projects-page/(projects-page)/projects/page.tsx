import React from "react";
import ProjectsPage from "@/app/apps/projects-page/(projects-page)/_components/ProjectsPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Projects - PolytronX",
    description:
      "Explore advanced project management and tracking features for your web applications.",
    keywords: [
      "projects",
      "project management",
      "react projects",
      "UI projects",
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
      "task management",
    ],
    openGraph: {
      title: "Projects - PolytronX",
      description:
        "Explore advanced project management and tracking features for your web applications.",
      url: "/apps/projects",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ProjectsPage />
    </div>
  );
};

export default Page;
