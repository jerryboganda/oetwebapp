import { getLocalStorageItem, hexToRGB } from "../../../_helper/index";

export const verticalBarChart = {
  labels: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul"],
  datasets: [
    {
      label: "Dataset #1",
      backgroundColor: hexToRGB(
        getLocalStorageItem("color-primary", "#8973EA"),
        0.5
      ),
      borderColor: hexToRGB(getLocalStorageItem("color-primary", "#8973EA"), 1),
      data: [-65, 59, -20, 81, 56, -55, 40],
    },
    {
      label: "Dataset #2",
      backgroundColor: hexToRGB(
        getLocalStorageItem("color-primary", "#8973EA"),
        0.5
      ),
      borderColor: "rgba(299, 94, 64,1)",
      data: [65, 59, -20, 81, -56, 55, -40],
    },
  ],
  options: {
    scales: {
      y: {
        beginAtZero: true,
      },
    },
    plugins: {
      legend: {
        display: false,
      },
    },
  },
};
