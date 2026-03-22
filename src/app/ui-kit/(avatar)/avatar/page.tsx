import React from "react";
import AvatarPage from "@/app/ui-kit/(avatar)/_components/AvatarPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Avatars - PolytronX",
    description:
      "Explore avatar components for user profile images and icons in your React application.",
    keywords: [
      "avatars",
      "react components",
      "UI components",
      "profile images",
      "user icons",
      "react avatar",
      "UI avatar",
      "component customization",
      "react optimization",
      "component performance",
      "avatar design",
      "UI integration",
      "profile components",
      "user components",
    ],
    openGraph: {
      title: "Avatars - PolytronX",
      description:
        "Explore avatar components for user profile images and icons in your React application.",
      url: "/ui-kit/avatar",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AvatarPage />
    </div>
  );
};

export default Page;
