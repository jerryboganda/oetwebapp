import React from 'react';
import type { ReactNode } from 'react';

export const ResponsiveContainer = ({ children }: { children?: ReactNode }) => (
  <div data-testid="responsive-chart">{children}</div>
);
export const BarChart = ({ children }: { children?: ReactNode }) => (
  <div data-testid="bar-chart">{children}</div>
);
export const LineChart = ({ children }: { children?: ReactNode }) => (
  <div data-testid="line-chart">{children}</div>
);
export const AreaChart = ({ children }: { children?: ReactNode }) => (
  <div data-testid="area-chart">{children}</div>
);
export const PieChart = ({ children }: { children?: ReactNode }) => (
  <div data-testid="pie-chart">{children}</div>
);
export const RadarChart = ({ children }: { children?: ReactNode }) => (
  <div data-testid="radar-chart">{children}</div>
);
export const Bar = () => null;
export const Line = () => null;
export const Area = () => null;
export const Pie = () => null;
export const Cell = () => null;
export const CartesianGrid = () => null;
export const Tooltip = () => null;
export const Legend = () => null;
export const XAxis = () => null;
export const YAxis = () => null;
export const Sector = () => null;
export const Radar = () => null;
export const PolarGrid = () => null;
export const PolarAngleAxis = () => null;
export const PolarRadiusAxis = () => null;
