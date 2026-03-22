import { getLocalStorageItem, hexToRGB } from "../../../_helper/index";
export const PolarAreaData = {
  labels: ["Jan", "Feb", "Mar", "Apr", "May"],
  datasets: [
    {
      label: "Dataset #1",
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-primary", "#8973EA"), 0.5),
        "rgba(46,94,231,0.5)",
        "rgba( 215, 220, 65,0.5)",
      ],
      data: [-10, -54, 40, 20, 56, 55, -40],
    },
  ],
};
