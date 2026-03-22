"use server";

import { redirect } from "next/navigation";

export async function unlockScreen(_formData: FormData) {
  redirect("/");
}

export async function unlockScreenImg(_formData: FormData) {
  redirect("/");
}

export async function createPassword(_formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  redirect("/auth-pages/sign-in");
}

export async function resetPassword(_formData: FormData) {
  redirect("/");
}

export async function loginUser(_formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  redirect("/");
}

export async function loginUserImg(_formData: FormData) {
  await new Promise((res) => setTimeout(res, 300));
  redirect("/");
}

export async function verifyOtp(_formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  redirect("/");
}
