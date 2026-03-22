import React from "react";
import ModalPage from "@/app/advance-ui/(modal)/_components/ModalPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Modal - PolytronX",
    description:
      "Explore advanced modal components and dialog boxes for your web applications.",
    keywords: [
      "modal",
      "dialog",
      "popup",
      "modal dialog",
      "react modal",
      "UI modal",
      "modal window",
      "modal overlay",
      "modal customization",
      "modal events",
      "modal styles",
      "modal animation",
      "modal integration",
      "modal library",
      "react dialog",
    ],
    openGraph: {
      title: "Modal - PolytronX",
      description:
        "Explore advanced modal components and dialog boxes for your web applications.",
      url: "/advance-ui/modal",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ModalPage />
    </div>
  );
};

export default Page;
