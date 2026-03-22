import React from "react";
import FileUploadPage from "@/app/forms-elements/(file-upload)/_components/FileUploadPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "File Upload Forms - PolytronX",
    description:
      "Explore file upload components for handling file uploads in your React forms.",
    keywords: [
      "file upload",
      "file input",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "file handling",
      "upload customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "file validation",
      "drag and drop",
    ],
    openGraph: {
      title: "File Upload Forms - PolytronX",
      description:
        "Explore file upload components for handling file uploads in your React forms.",
      url: "/forms-elements/file-upload",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FileUploadPage />
    </div>
  );
};

export default Page;
