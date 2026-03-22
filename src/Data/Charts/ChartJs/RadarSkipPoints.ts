import { getLocalStorageItem, hexToRGB } from "../../../_helper/index";

export const RadarSkipPoints = {
  labels: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul"],
  datasets: [
    {
      label: "Dataset #1",
      backgroundColor: "rgba(140, 118, 240,0.2)",
      borderColor: "rgba( 240,10,200,1)",
      data: [-20, 25, -20, -5, 35, -10, 20],
    },
    {
      label: "Dataset #2",
      backgroundColor: hexToRGB(
        getLocalStorageItem("color-primary", "#147534"),
        0.2
      ),
      borderColor: hexToRGB(getLocalStorageItem("color-primary", "#147534"), 1),
      data: [-20, -23, 20, 0, 8, 25, -20],
    },
  ],
  options: {
    scales: {
      y: {
        beginAtZero: true,
      },
    },
  },
};
