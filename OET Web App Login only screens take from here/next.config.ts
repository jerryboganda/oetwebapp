import { NextConfig } from "next";

/** @type {NextConfig} */
const nextConfig: NextConfig = {
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
        destination: "/login",
        permanent: true,
      },
      {
        source: "/other-pages/terms-condition",
        destination: "/terms",
        permanent: true,
      },
    ];
  },
};

export default nextConfig;
