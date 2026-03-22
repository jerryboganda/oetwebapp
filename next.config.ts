import path from "path";
import { NextConfig } from "next";

/** @type {NextConfig} */
const nextConfig: NextConfig = {
  sassOptions: {
    includePaths: [path.join(__dirname, "styles")],
    quietDeps: true,
    silenceDeprecations: ["import", "global-builtin", "legacy-js-api"],
  },
  webpack: (config) => {
    config.cache = false;
    return config;
  },
  experimental: {
    serverActions: {},
    webpackBuildWorker: true,
  },
  async redirects() {
    return [
      {
        source: "/",
        destination: "/dashboard/project",
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
