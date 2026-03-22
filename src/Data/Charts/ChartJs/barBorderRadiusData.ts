import { getLocalStorageItem, hexToRGB } from "@/_helper/index";

interface Dataset {
  label: string;
  backgroundColor: string;
  borderColor: string;
  borderWidth?: number;
  borderRadius: number;
  borderSkipped: boolean;
  data: number[];
}

interface Options {
  scales: {
    y: {
      beginAtZero: boolean;
    };
  };
  plugins: {
    legend: {
      display: boolean;
    };
  };
}

interface BarBorderRadiusData {
  labels: string[];
  datasets: Dataset[];
  options: Options;
}

export const barBorderRadiusData: BarBorderRadiusData = {
  labels: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul"],
  datasets: [
    {
      label: "Dataset #1",
      backgroundColor: hexToRGB(
        getLocalStorageItem("color-primary", "#8973EA"),
        0.2
      ),
      borderColor: hexToRGB(getLocalStorageItem("color-primary", "#8973EA"), 1),
      borderWidth: 2,
      borderRadius: 5,
      borderSkipped: false,
      data: [-65, 59, -20, 81, 56, -55, 40],
    },
    {
      label: "Dataset #2",
      backgroundColor: hexToRGB(
        getLocalStorageItem("color-primary", "#E90BC4"),
        0.2
      ),
      borderColor: hexToRGB(
        getLocalStorageItem("color-primary", "#E90BC4"),
        0.2
      ),
      borderRadius: 50,
      borderSkipped: false,
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
