import { Chart, ChartOptions, ChartDataset } from "chart.js";
import { getLocalStorageItem, hexToRGB } from "@/_helper";
// @ts-expect-error
import type { TypedChartComponent } from "react-chartjs-2/dist/types";

type PieChartData = {
  labels: string[];
  datasets: ChartDataset<"pie">[];
  options: ChartOptions<"pie">;
};

export const MultiSeriesPie: TypedChartComponent<"pie"> & PieChartData = {
  labels: [
    "Overall Yay",
    "Overall Nay",
    "Group A Yay",
    "Group A Nay",
    "Group B Yay",
    "Group B Nay",
    "Group C Yay",
    "Group C Nay",
  ],
  datasets: [
    {
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-primary", "#8973EA"), 0.1),
        hexToRGB(getLocalStorageItem("color-primary", "#8973EA")),
      ],
      data: [21, 79],
    },
    {
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-secondary", "#626263"), 0.1),
        hexToRGB(getLocalStorageItem("color-success", "#626263")),
      ],
      data: [33, 67],
    },
    {
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-success", "#147534"), 0.5),
        hexToRGB(getLocalStorageItem("color-success", "#147534"), 1),
      ],
      data: [20, 80],
    },
    {
      backgroundColor: [
        hexToRGB(getLocalStorageItem("color-danger", "#E90BC4"), 0.5),
        hexToRGB(getLocalStorageItem("color-danger", "#E90BC4"), 1),
      ],
      data: [10, 90],
    },
  ],
  options: {
    responsive: true,
    plugins: {
      legend: {
        labels: {
          generateLabels: function (chart: Chart) {
            const original =
              Chart.defaults.plugins.legend.labels.generateLabels;
            const labelsOriginal = original.call(this, chart);

            labelsOriginal.forEach((label) => {
              label.datasetIndex = Math.floor((label.index ?? 0) / 2);
              label.hidden = !chart.isDatasetVisible(label.datasetIndex);
            });

            return labelsOriginal;
          },
        },
      },
      tooltip: {
        callbacks: {
          label: function (context: { label: never; formattedValue: never }) {
            return `${context.label}: ${context.formattedValue}`;
          },
        },
      },
    },
  },
};
