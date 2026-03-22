import { getLocalStorageItem, hexToRGB } from "../../../_helper/index";

export const PieData = {
  labels: ["Jan", "Feb", "Mar", "Apr", "May"],
  datasets: [
    {
      label: "Dataset #1",
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-dark", "#626263"), 0.5),
        "rgba(46,94,231,0.5)",
        "rgba( 240,10,200,0.5)",
      ],
      data: [-20, -54, 20, 0, 56, 55, -40],
    },
  ],
};
