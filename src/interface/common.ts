import * as React from "react";

export interface SidebarProps {
  sidebarOpen?: boolean;
  setSidebarOpen?: (isOpen: boolean) => void;
}

export interface ProfileProps {
  data: string;
  setData: React.Dispatch<React.SetStateAction<string>>;
}

export interface MenuItemProps {
  title?: string | undefined;
  iconClass:
    | React.ForwardRefExoticComponent<
        Omit<React.SVGProps<SVGSVGElement>, "ref"> &
          React.RefAttributes<SVGSVGElement>
      >
    | string;
  type?: string | undefined;
  path?: string | undefined;
  badgeCount?: string | number | undefined;
  links?:
    | Array<{
        path: string;
        name: string;
        collapseId?: string | undefined;
        children?:
          | Array<{
              path: string;
              name: string;
            }>
          | undefined;
      }>
    | undefined;
  name?: string | undefined;
  collapseId?: string | undefined;
  children?:
    | Array<{
        path: string;
        name: string;
        collapseId?: string | undefined;
        children?:
          | Array<{
              path: string;
              name: string;
            }>
          | undefined;
      }>
    | undefined;
}

export interface LinkInterface {
  path: string;
  name: string;
  collapseId?: string | undefined;
  children?: LinkInterface[] | undefined; // Recursive type for nested links
}

interface Badge {
  positionClass: string;
  bgColor: string;
  badgeText?: JSX.Element | string;
  animationClass?: string;
}

export interface MenuItem {
  href: string;
  icon: string;
  badge?: Badge | null;
  color: string;
  text: string;
  textColor: string;
}
