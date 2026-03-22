import React from "react";
import TeamPage from "@/app/apps/(team)/_components/TeamPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Team - PolytronX",
    description:
      "Manage your team members and collaborate effectively with our team management system.",
    keywords: [
      "team management",
      "team collaboration",
      "team members",
      "team organization",
      "team roles",
      "team permissions",
      "team settings",
      "team projects",
      "team communication",
      "team tasks",
      "team calendar",
      "team files",
      "team activities",
      "team reporting",
      "team analytics",
    ],
    openGraph: {
      title: "Team - PolytronX",
      description:
        "Manage your team members and collaborate effectively with our team management system.",
      url: "/apps/team",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TeamPage />
    </div>
  );
};

export default Page;
