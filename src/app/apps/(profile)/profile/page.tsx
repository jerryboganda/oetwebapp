import React from "react";
import ProfilePage from "@/app/apps/(profile)/_components/ProfilePage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Profile - PolytronX",
    description:
      "Manage your profile settings, update personal information, and customize your dashboard preferences.",
    keywords: [
      "profile settings",
      "user management",
      "dashboard customization",
      "PolytronX",
      "user profile",
      "account settings",
      "personal information",
      "preferences",
    ],
    openGraph: {
      title: "Profile - PolytronX",
      description:
        "Manage your profile settings and preferences in PolytronX dashboard.",
      url: "/apps/profile",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return <ProfilePage />;
};
export default Page;
