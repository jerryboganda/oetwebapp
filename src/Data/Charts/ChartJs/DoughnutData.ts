import { getLocalStorageItem, hexToRGB } from "@/_helper/index";

export const DoughnutData = {
  labels: ["Jan", "Feb", "Mar", "Apr", "May"],
  datasets: [
    {
      label: "Dataset #1",
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-primary", "#8973EA"), 0.5),
        "rgba(20, 120, 52,0.5)",
        "rgba( 240,10,200,0.5)",
      ],
      data: [-20, -54, 20, 0, 56, 55, -40],
    },
  ],
};
