declare module "weather-icons-react" {
  import React from "react";

  export interface WeatherIconProps {
    size?: number;
    color?: string;
  }

  const WeatherIcon: React.FC<WeatherIconProps>;

  export default WeatherIcon;
}

type IconComponent =
  | FC<PhosphorIconProps>
  | React.ComponentType<React.SVGProps<SVGSVGElement>>;
