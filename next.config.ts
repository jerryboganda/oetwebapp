import path from "path";
import { NextConfig } from "next";

/** @type {NextConfig} */
const nextConfig: NextConfig = {
  sassOptions: {
    includePaths: [path.join(__dirname, "styles")],
    quietDeps: true,
    silenceDeprecations: ["import", "global-builtin", "legacy-js-api"],
  },
  experimental: {
    serverActions: {},
  },
  async redirects() {
    return [
      {
        source: "/auth-pages/sign-in-with-bg-image",
        destination: "/login",
        permanent: true,
      },
      {
        source: "/auth-pages/sign-up-with-bg-image",
        destination: "/register",
        permanent: true,
      },
      {
        source: "/auth-pages/sign-up-success",
        destination: "/register/success",
        permanent: true,
      },
      {
        source: "/auth-pages/password-reset-img",
        destination: "/forgot-password",
        permanent: true,
      },
      {
        source: "/auth-pages/password-reset-otp-img",
        destination: "/forgot-password/verify",
        permanent: true,
      },
      {
        source: "/auth-pages/password-create-img",
        destination: "/reset-password",
        permanent: true,
      },
      {
        source: "/auth-pages/password-reset-success-img",
        destination: "/reset-password/success",
        permanent: true,
      },
      {
        source: "/auth-pages/two-step-verification-img",
        destination: "/verify",
        permanent: true,
      },
      {
        source: "/auth-pages/lock-screen-img",
        destination: "/lock-screen",
        permanent: true,
      },
    ];
  },
  // async headers() {
  //   return [
  //     {
  //       source: "/images/:path*",
  //       headers: [
  //         {
  //           key: "Cache-Control",
  //           value: "public, max-age=3600, immutable",
  //         },
  //       ],
  //     },
  //     {
  //       source: "/image/:path*",
  //       headers: [
  //         {
  //           key: "Cache-Control",
  //           value: "public, max-age=3600, immutable",
  //         },
  //       ],
  //     },
  //     {
  //       source: "/:path*",
  //       headers: [
  //         {
  //           key: "Cache-Control",
  //           value: "public, max-age=3600, must-revalidate",
  //         },
  //       ],
  //     },
  //   ];
  // },
};

export default nextConfig;
