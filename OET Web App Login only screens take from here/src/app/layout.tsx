import type { ReactNode } from "react";
import { Montserrat } from "next/font/google";
import type { Metadata, Viewport } from "next";
import "./globals.css";

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
    default: "OET Auth",
    template: "%s | OET",
  },
  description:
    "A focused OET authentication workspace with sign-in, sign-up, OTP, and password reset flows.",
  icons: {
    icon: "/oet-mark.svg",
    shortcut: "/oet-mark.svg",
    apple: "/oet-mark.svg",
  },
  metadataBase: new URL(
    process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3001"
  ),
  openGraph: {
    type: "website",
    locale: "en_US",
    url: "/",
    siteName: "OET Auth",
    title: "OET Auth",
    description:
      "A focused OET authentication workspace with sign-in, sign-up, OTP, and password reset flows.",
  },
  twitter: {
    card: "summary_large_image",
    title: "OET Auth",
    description:
      "A focused OET authentication workspace with sign-in, sign-up, OTP, and password reset flows.",
  },
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html
      lang="en"
      className={`${montserrat.variable} font-sans`}
      suppressHydrationWarning
    >
      <body suppressHydrationWarning>{children}</body>
    </html>
  );
}
