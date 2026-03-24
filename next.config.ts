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
      {
        source: "/app/:path*",
        destination: "/learner/:path*",
        permanent: true,
      },
      {
        source: "/expert/:path*",
        destination: "/reviewer/:path*",
        permanent: true,
      },
      {
        source: "/admin/:path*",
        destination: "/cms/:path*",
        permanent: true,
      },
    ];
  },
  async rewrites() {
    return [
      {
        source: "/learner/:path*",
        destination: "/app/:path*",
      },
      {
        source: "/reviewer/:path*",
        destination: "/expert/:path*",
      },
      {
        source: "/cms/:path*",
        destination: "/admin/:path*",
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
