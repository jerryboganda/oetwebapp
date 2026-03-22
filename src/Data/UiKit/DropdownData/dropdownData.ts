interface DropdownConfig {
  label: string;
  color: string;
  items: string[];
  asLink?: boolean;
}

const dropdownData: DropdownConfig[] = [
  {
    label: "Dropdown button",
    color: "primary",
    items: ["Action", "Another action", "Something else here"],
    asLink: false,
  },
  {
    label: "Dropdown link",
    color: "secondary",
    items: ["Action", "Another action", "Something else here"],
    asLink: true,
  },
];
