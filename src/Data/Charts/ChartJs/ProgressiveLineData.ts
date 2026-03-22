import {
  CartesianScaleTypeRegistry,
  ChartOptions,
  ScaleOptionsByType,
} from "chart.js";
import { getLocalStorageItem, hexToRGB } from "@/_helper";

const data = [];
const data2 = [];
let prev = 100;
let prev2 = 80;

for (let i = 0; i < 1000; i++) {
  prev += 5 - Math.random() * 10;
  data.push({ x: i, y: prev });
  prev2 += 5 - Math.random() * 10;
  data2.push({ x: i, y: prev2 });
}

const totalDuration = 10000;
const delayBetweenPoints = totalDuration / data.length;

export const ProgressiveLineData = {
  datasets: [
    {
      borderColor: "rgba(240,10,200,1)",
      borderWidth: 1,
      radius: 0,
      data: data,
    },
    {
      borderColor: hexToRGB(getLocalStorageItem("color-primary", "#8973EA"), 1),
      borderWidth: 1,
      radius: 0,
      data: data2,
    },
  ],
};

export const ProgressiveLineOptions: ChartOptions<"line"> = {
  animation: {
    easing: "linear",
    duration: delayBetweenPoints,
  },
  interaction: {
    intersect: false,
  },
  plugins: {
    legend: {
      display: false,
    },
  },
  scales: {
    x: {
      type: "linear",
    } as ScaleOptionsByType<keyof CartesianScaleTypeRegistry>,
  },
};
