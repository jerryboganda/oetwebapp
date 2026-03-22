import type { Metadata } from "next";
import ProjectDashboard from "@/Component/Dashboard/ProjectDashboard";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Project Dashboard - PolytronX",
    description:
      "Manage and track your projects with our comprehensive dashboard featuring real-time project analytics and insights.",
    keywords: [
      "project dashboard",
      "dashboard",
      "react dashboard",
      "UI dashboard",
      "dashboard component",
      "project management",
      "task dashboard",
      "business dashboard",
      "dashboard customization",
      "dashboard styles",
      "dashboard effects",
      "dashboard integration",
      "dashboard library",
      "react dashboard app",
      "dashboard optimization",
      "dashboard performance",
      "project analytics",
    ],
    openGraph: {
      title: "Project Dashboard - PolytronX",
      description:
        "Manage and track your projects with our comprehensive dashboard featuring real-time project analytics and insights.",
      url: "/dashboard/project",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ProjectDashboard />
    </div>
  );
};

export default Page;
