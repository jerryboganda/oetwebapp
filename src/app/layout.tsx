import React from "react";
import { Montserrat } from "next/font/google";
import type { Metadata, Viewport } from "next";
import "slick-carousel/slick/slick.css";
import "slick-carousel/slick/slick-theme.css";
import "animate.css";
import "filepond/dist/filepond.min.css";
import "simplebar-react/dist/simplebar.min.css";
import "react-quill-new/dist/quill.snow.css";
import "bootstrap/dist/css/bootstrap.min.css";
import "select2/src/scss/_dropdown.scss";
import "select2/src/scss/_multiple.scss";
import "select2/src/scss/core.scss";
import "@/assets/scss/style.scss";
import "@/assets/css/style.css";
import "@/assets/scss/responsive.scss";
import "datatables.net-dt/css/dataTables.dataTables.css";
import "datatables.net-dt/css/dataTables.dataTables.min.css";
import DefaultLayout from "@/Component/Layouts/DefaultLayout";
import DocumentTitleManager from "@/Component/DocumentTitleManager";

const montserrat = Montserrat({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700", "800", "900"],
  display: "swap",
  variable: "--font-montserrat",
});

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 1,
  userScalable: false,
  themeColor: "#ffffff",
};

export const metadata: Metadata = {
  title: {
    default: "PolytronX Admin Dashboard",
    template: "%s | PolytronX",
  },
  description:
    "A modern, flexible admin dashboard for analytics, operations, and team workflows.",
  keywords: [
    "admin dashboard",
    "PolytronX dashboard",
    "dashboard workspace",
    "admin workspace",
    "responsive dashboard",
    "web app",
  ],
  icons: {
    icon: "/images/logo/polytronx-mark.svg",
    shortcut: "/images/logo/polytronx-mark.svg",
    apple: "/images/logo/polytronx-mark.svg",
  },
  metadataBase: new URL(
    process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3000"
  ),
  openGraph: {
    type: "website",
    locale: "en_US",

    url: "/",
    siteName: "PolytronX",
    title: "PolytronX Admin Dashboard",
    description:
      "A modern, flexible admin dashboard for analytics, operations, and team workflows.",
  },
  twitter: {
    card: "summary_large_image",
    title: "PolytronX Admin Dashboard",
    description:
      "A modern, flexible admin dashboard for analytics, operations, and team workflows.",
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" className={`${montserrat.variable} font-sans`}>
      <head>
        <link
          rel="stylesheet"
          href="https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@latest/tabler-icons.min.css"
          crossOrigin="anonymous"
        />
      </head>
      <body className="ltr light" suppressHydrationWarning>
        <DefaultLayout>
          <DocumentTitleManager
            defaultTitle="PolytronX Admin Dashboard"
            blurTitle="Come back to PolytronX"
          />
          {children}
        </DefaultLayout>
      </body>
    </html>
  );
}
