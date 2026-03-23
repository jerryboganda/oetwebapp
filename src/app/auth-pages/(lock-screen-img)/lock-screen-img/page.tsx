import React from "react";
import LockScreenImgPage from "@/app/auth-pages/(lock-screen-img)/_components/LockScreenImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Lock Screen with Background - PolytronX",
    description:
      "Secure your session with our visually appealing lock screen interface featuring a background image.",
    keywords: [
      "lock screen",
      "session lock",
      "security",
      "react lock screen",
      "UI lock screen",
      "lock screen component",
      "session security",
      "lock screen customization",
      "lock screen styles",
      "background image",
      "auth background",
      "lock screen effects",
      "lock screen integration",
      "lock screen library",
      "react auth",
      "lock screen optimization",
      "lock screen performance",
      "session protection",
    ],
    openGraph: {
      title: "Lock Screen with Background - PolytronX",
      description:
        "Secure your session with our visually appealing lock screen interface featuring a background image.",
      url: "/lock-screen",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <LockScreenImgPage />
    </div>
  );
};

export default Page;
