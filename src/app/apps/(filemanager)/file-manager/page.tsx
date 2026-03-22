import React from "react";
import FilemanagerPage from "@/app/apps/(filemanager)/_components/FilemanagerPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "File Manager - PolytronX",
    description:
      "Manage your files and documents with our intuitive file management system.",
    keywords: [
      "file manager",
      "document management",
      "file storage",
      "file organization",
      "file upload",
      "file sharing",
      "file preview",
      "document viewer",
      "file search",
      "file sync",
      "cloud storage",
      "file backup",
      "file permissions",
      "folder management",
      "file operations",
    ],
    openGraph: {
      title: "File Manager - PolytronX",
      description:
        "Manage your files and documents with our intuitive file management system.",
      url: "/apps/file-manager",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FilemanagerPage />
    </div>
  );
};

export default Page;
