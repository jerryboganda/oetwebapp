import React, { useEffect, useState } from "react";
import { timeSpentData } from "@/Data/Charts/ApexCharts/ApexChart";

const TimeSpent = () => {
  const [Chart, setChart] = useState<any>(null);

  useEffect(() => {
    if (typeof window !== "undefined") {
      import("react-apexcharts").then((mod) => {
        setChart(() => mod.default);
      });
    }
  }, []);

  if (!Chart) return null;

  return (
    <Chart
      options={timeSpentData}
      series={timeSpentData.series}
      type="line"
      height={280}
    />
  );
};

export default TimeSpent;
