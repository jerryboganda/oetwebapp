"use server";

export async function handleCustomTextarea(formData: FormData) {
  const input = formData.get("myTextarea");
  return String(input ?? "");
}
