"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { getBottomNavigationItemsForRole } from "@/Data/Sidebar/oetNavigation";

const navItems = getBottomNavigationItemsForRole("learner");

const LearnerBottomNav = () => {
  const pathname = usePathname();

  if (!pathname.startsWith("/learner") && !pathname.startsWith("/app")) {
    return null;
  }

  return (
    <div className="d-lg-none fixed-bottom bg-white border-top shadow-sm">
      <div className="d-flex justify-content-around py-2">
        {navItems.map((item) => {
          const isActive =
            !!item.path &&
            (pathname === item.path || pathname.startsWith(`${item.path}/`));

          return (
            <Link
              key={item.name}
              href={item.path || "/learner/dashboard"}
              className={`text-decoration-none text-center ${
                isActive ? "text-primary" : "text-secondary"
              }`}
            >
              <div className="f-s-12">{item.name}</div>
            </Link>
          );
        })}
      </div>
    </div>
  );
};

export default LearnerBottomNav;
