import { redirect } from "next/navigation";
import { AUTH_ROUTES } from "@/lib/auth/routes";

export default function HomePage() {
  redirect(AUTH_ROUTES.signIn);
}
