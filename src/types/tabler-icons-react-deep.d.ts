declare module "@tabler/icons-react/dist/esm/icons/*.mjs" {
  import type { ForwardRefExoticComponent, RefAttributes } from "react";
  import type { IconProps } from "@tabler/icons-react/dist/tabler-icons-react";

  const icon: ForwardRefExoticComponent<
    IconProps & RefAttributes<SVGSVGElement>
  >;

  export default icon;
}

declare module "@tabler/icons-react/dist/esm/icons-list.mjs" {
  const iconsList: string[];
  export default iconsList;
}
