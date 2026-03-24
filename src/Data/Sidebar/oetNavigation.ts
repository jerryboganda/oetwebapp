import {
  Book,
  Brain,
  Calendar,
  Folder,
  HomeAlt,
  MenuScale,
  MessageText,
  PageEdit,
  Reports,
  Settings,
} from "iconoir-react";
import type { ForwardRefExoticComponent, RefAttributes, SVGProps } from "react";
import type { OetRole } from "@/types/oet";

type IconComponent = ForwardRefExoticComponent<
  Omit<SVGProps<SVGSVGElement>, "ref"> & RefAttributes<SVGSVGElement>
>;

export interface OetNavigationItem {
  type?: string;
  title?: string;
  iconClass: IconComponent;
  name: string;
  path?: string;
  badgeCount?: string | number;
  collapseId?: string;
  children?: Array<{
    name: string;
    path: string;
    collapseId?: string;
    children?: Array<{
      name: string;
      path: string;
    }>;
  }>;
}

export interface OetBottomNavItem {
  name: string;
  path: string;
}

const learnerNavigation: OetNavigationItem[] = [
  {
    type: "dropdown",
    title: "Learner",
    iconClass: HomeAlt,
    name: "Workspace",
    collapseId: "oet-learner-workspace",
    path: "/learner/dashboard",
    children: [
      { name: "Dashboard", path: "/learner/dashboard" },
      { name: "Onboarding", path: "/learner/onboarding" },
    ],
  },
  {
    type: "dropdown",
    iconClass: Calendar,
    name: "Planning",
    collapseId: "oet-learner-planning",
    path: "/learner/goals",
    children: [
      { name: "Goals", path: "/learner/goals" },
      { name: "Diagnostic", path: "/learner/diagnostic" },
      { name: "Study Plan", path: "/learner/study-plan" },
      { name: "Readiness", path: "/learner/readiness" },
      { name: "Progress", path: "/learner/progress" },
    ],
  },
  {
    type: "dropdown",
    title: "Core Skills",
    iconClass: Book,
    name: "Reading",
    collapseId: "oet-learner-reading",
    path: "/learner/reading",
    children: [
      { name: "Reading Overview", path: "/learner/reading" },
      { name: "Reading Task", path: "/learner/reading/task/reading-task-1" },
    ],
  },
  {
    type: "dropdown",
    iconClass: Brain,
    name: "Listening",
    collapseId: "oet-learner-listening",
    path: "/learner/listening",
    children: [
      { name: "Listening Overview", path: "/learner/listening" },
      {
        name: "Listening Task",
        path: "/learner/listening/task/listening-task-1",
      },
    ],
  },
  {
    type: "dropdown",
    iconClass: PageEdit,
    name: "Writing",
    collapseId: "oet-learner-writing",
    path: "/learner/writing",
    children: [
      { name: "Writing Overview", path: "/learner/writing" },
      { name: "Writing Tasks", path: "/learner/writing/tasks" },
      {
        name: "Model Answer",
        path: "/learner/writing/model-answer/writing-task-1",
      },
    ],
  },
  {
    type: "dropdown",
    iconClass: MessageText,
    name: "Speaking",
    collapseId: "oet-learner-speaking",
    path: "/learner/speaking",
    children: [
      { name: "Speaking Overview", path: "/learner/speaking" },
      { name: "Speaking Tasks", path: "/learner/speaking/tasks" },
      { name: "Mic Check", path: "/learner/speaking/mic-check" },
    ],
  },
  {
    type: "dropdown",
    title: "Assessment",
    iconClass: Reports,
    name: "Mocks & Reviews",
    collapseId: "oet-learner-assessment",
    path: "/learner/mocks",
    children: [
      { name: "Mock Center", path: "/learner/mocks" },
      { name: "Reviews", path: "/learner/reviews" },
      { name: "History", path: "/learner/history" },
    ],
  },
  {
    type: "dropdown",
    title: "Account",
    iconClass: Settings,
    name: "Account",
    collapseId: "oet-learner-account",
    path: "/learner/settings",
    children: [
      { name: "Billing", path: "/learner/billing" },
      { name: "Settings", path: "/learner/settings" },
    ],
  },
];

const expertNavigation: OetNavigationItem[] = [
  {
    type: "dropdown",
    title: "Expert",
    iconClass: MenuScale,
    name: "Review Operations",
    collapseId: "oet-expert-workspace",
    path: "/expert/queue",
    children: [
      { name: "Queue", path: "/expert/queue" },
      { name: "Calibration", path: "/expert/calibration" },
      { name: "Metrics", path: "/expert/metrics" },
      { name: "Schedule", path: "/expert/schedule" },
    ],
  },
];

const adminNavigation: OetNavigationItem[] = [
  {
    type: "dropdown",
    title: "Admin",
    iconClass: Folder,
    name: "Content & Taxonomy",
    collapseId: "oet-admin-content",
    path: "/admin/content",
    children: [
      { name: "Content Library", path: "/admin/content" },
      { name: "Task Builder", path: "/admin/content/new" },
      { name: "Taxonomy", path: "/admin/taxonomy" },
      { name: "Target Countries", path: "/admin/taxonomy/countries" },
      { name: "Exam Types", path: "/admin/taxonomy/exams" },
      { name: "Professions", path: "/admin/taxonomy/professions" },
      { name: "Sessions", path: "/admin/taxonomy/sessions" },
    ],
  },
  {
    type: "dropdown",
    iconClass: Settings,
    name: "Governance",
    collapseId: "oet-admin-governance",
    path: "/admin/criteria",
    children: [
      { name: "Criteria", path: "/admin/criteria" },
      { name: "AI Config", path: "/admin/ai-config" },
      { name: "Review Ops", path: "/admin/review-ops" },
      { name: "Quality Analytics", path: "/admin/analytics/quality" },
      { name: "User Ops", path: "/admin/users" },
      { name: "Billing Ops", path: "/admin/billing" },
      { name: "Feature Flags", path: "/admin/flags" },
      { name: "Audit Logs", path: "/admin/audit-logs" },
    ],
  },
];

const learnerBottomNav: OetBottomNavItem[] = [
  { name: "Dashboard", path: "/learner/dashboard" },
  { name: "Study Plan", path: "/learner/study-plan" },
  { name: "Writing", path: "/learner/writing" },
  { name: "Speaking", path: "/learner/speaking" },
  { name: "Progress", path: "/learner/progress" },
];

const roleNavigationMap: Record<OetRole, OetNavigationItem[]> = {
  learner: learnerNavigation,
  expert: expertNavigation,
  admin: adminNavigation,
};

const roleBottomNavMap: Record<OetRole, OetBottomNavItem[]> = {
  learner: learnerBottomNav,
  expert: [],
  admin: [],
};

export function getNavigationItemsForRole(role: OetRole): OetNavigationItem[] {
  return roleNavigationMap[role];
}

export function getBottomNavigationItemsForRole(
  role: OetRole
): OetBottomNavItem[] {
  return roleBottomNavMap[role];
}
