export const badgeColors = [
  "primary",
  "secondary",
  "success",
  "danger",
  "warning",
  "info",
  "light",
  "dark",
];
export const outlineBadgeColors = [
  "primary",
  "secondary",
  "success",
  "danger",
  "warning",
  "info",
  "dark",
];
export const lightBadgeColors = [
  { color: "primary", icon: "ti-download" },
  { color: "secondary", icon: "" },
  { color: "success", icon: "" },
  { color: "danger", icon: "" },
  { color: "warning", icon: "" },
  { color: "info", icon: "" },
  { color: "dark", icon: "" },
];
export const radiusBadgeData = [
  { color: "primary", radius: "0" },
  { color: "secondary", radius: "6" },
  { color: "success", radius: "8" },
  { color: "danger", radius: "10" },
];
interface BadgePosition {
  label: string;
  bgColor: string;
  positionClass: string;
}
export const badgePositionData: BadgePosition[] = [
  { label: "Offline", bgColor: "danger", positionClass: "top-0 start-0" },
  { label: "Busy", bgColor: "warning", positionClass: "top-0 start-100" },
  { label: "Online", bgColor: "success", positionClass: "top-100 start-0" },
  {
    label: "Disable",
    bgColor: "secondary",
    positionClass: "top-100 start-100",
  },
];
export const headingData = [
  { level: "h1", label: "Heading Title" },
  { level: "h2", label: "Heading Title" },
  { level: "h3", label: "Heading Title" },
  { level: "h4", label: "Heading Title" },
  { level: "h5", label: "Heading Title" },
  { level: "h6", label: "Heading Title" },
];
