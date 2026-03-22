import { ApexOptions } from "apexcharts";

const chartOptions1 = {
  series: [
    {
      name: "series1",
      data: [5, 10, 8, 15, 12, 10, 20, 16, 20],
    },
  ],
  chart: {
    height: 150,
    type: "area",
    parentHeightOffset: 0,
    sparkline: {
      enabled: true,
    },
  },
  dataLabels: {
    enabled: false,
  },
  fill: {
    type: "gradient",
    colors: ["#056464"],
    gradient: {
      shadeIntensity: 1,
      opacityFrom: 0.4,
      opacityTo: 0.8,
      stops: [0, 90, 100],
    },
  },
  stroke: {
    width: 2,
    curve: "smooth",
  },
  yaxis: {
    show: false,
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
  },
  xaxis: {
    show: false,
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
  },
  grid: {
    show: false,
    padding: {
      top: 10,
      bottom: 2,
      left: -10,
      right: 0,
    },
    xaxis: {
      lines: {
        show: false,
      },
    },
    yaxis: {
      lines: {
        show: false,
      },
    },
  },
  tooltip: {
    enabled: false,
  },
};

const chartOptions2 = {
  series: [
    {
      name: "series1",
      data: [20, 25, 15, 12, 30, 25, 16, 28, 20],
    },
  ],
  chart: {
    height: 150,
    type: "area",
    parentHeightOffset: 0,
    sparkline: {
      enabled: true,
    },
  },
  colors: ["rgba(var(--success),1)"],
  dataLabels: {
    enabled: false,
  },
  stroke: {
    curve: "smooth",
    width: 2,
  },
  fill: {
    type: "gradient",
    colors: ["#0fb450"],
    gradient: {
      shadeIntensity: 1,
      opacityFrom: 0.4,
      opacityTo: 0.4,
      stops: [0, 90, 100],
    },
  },
  yaxis: {
    show: false,
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
  },
  xaxis: {
    show: false,
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
  },
  grid: {
    show: false,
    xaxis: {
      lines: {
        show: false,
      },
    },
    yaxis: {
      lines: {
        show: false,
      },
    },
    padding: {
      top: 10,
      bottom: 2,
      left: -10,
      right: 0,
    },
  },
  tooltip: {
    enabled: false,
  },
};

const chartOptions3 = {
  series: [
    {
      name: "series1",
      data: [5, 8, 10, 12, 10, 20, 18, 25, 22, 30],
    },
  ],
  chart: {
    height: 160,
    type: "area",
    parentHeightOffset: 0,
    sparkline: {
      enabled: true,
    },
  },
  colors: ["rgba(var(--success),1)"],
  dataLabels: {
    enabled: false,
  },
  stroke: {
    curve: "smooth",
    width: 2,
  },
  fill: {
    type: "gradient",
    gradient: {
      shadeIntensity: 0,
      opacityFrom: 1,
      opacityTo: 0.1,
      stops: [0, 90, 100],
    },
  },
  yaxis: {
    axisBorder: {
      show: false,
    },
  },
  xaxis: {
    categories: [
      "2014",
      "2015",
      "2016",
      "2017",
      "2018",
      "2019",
      "2020",
      "2021",
      "2022",
      "2023",
    ],
    axisBorder: {
      show: false,
    },
  },
  grid: {
    show: false,
    xaxis: {
      lines: {
        show: false,
      },
    },
    yaxis: {
      lines: {
        show: false,
      },
    },
    padding: {
      top: 10,
      bottom: 0,
      left: -10,
      right: 0,
    },
  },
  tooltip: {
    enabled: false,
  },
};

const chartOptions4 = {
  series: [
    {
      name: "data1",
      data: [45, 52, 38, 24, 33, 26, 21, 20, 6, 8, 15, 10],
    },
    {
      name: "data2",
      data: [87, 57, 74, 99, 75, 38, 62, 47, 82, 56, 45, 47],
    },
  ],
  chart: {
    height: 90,
    type: "line",
  },
  dataLabels: {
    enabled: false,
  },
  colors: ["rgba(var(--danger),1)", "rgba(var(--primary),1)"],
  stroke: {
    width: [3, 3],
    curve: "smooth",
    dashArray: [0, 5],
  },
  markers: {
    size: 0,
    hover: {
      sizeOffset: 6,
    },
  },
  legend: {
    show: false,
  },
  xaxis: {
    categories: [
      "01 Jan",
      "02 Jan",
      "03 Jan",
      "04 Jan",
      "05 Jan",
      "06 Jan",
      "07 Jan",
      "08 Jan",
      "09 Jan",
      "10 Jan",
      "11 Jan",
      "12 Jan",
    ],
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
    tooltip: {
      enabled: false,
    },
  },
  yaxis: {
    labels: {
      show: false,
    },
  },
  grid: {
    show: false,
    padding: {
      top: -10,
      right: 0,
      bottom: -18,
      left: 0,
    },
  },
  tooltip: {
    enabled: false,
  },
};

const chartOptions5 = {
  series: [80, 45, 67],
  chart: {
    height: 370,
    type: "radialBar",
  },
  colors: [
    "rgba(var(--primary),1)",
    "rgba(var(--danger),1)",
    "rgba(var(--warning),1)",
  ],
  plotOptions: {
    radialBar: {
      dataLabels: {
        name: {
          fontSize: "18px",
        },
        value: {
          fontSize: "20px",
          fontFamily: "Poppins, sans-serif",
          fontWeight: 500,
          color: "rgba(var(--primary),1)",
        },
        total: {
          show: true,
          label: "Total",
        },
      },
    },
  },
  labels: ["New Target", "Resolve Target", "Total"],
  responsive: [
    {
      breakpoint: 1250,
      options: {
        chart: {
          height: 300,
        },
      },
    },
  ],
};

const fileManagerOption = {
  series: [42, 47, 52, 58],
  labels: ["Data A", "Data B", "Data C", "Data D"],
  dataLabels: {
    enabled: false,
  },
  colors: ["#056464", "#74788d", "#ea5659", "#fac10f"],
  yaxis: {
    show: false,
  },
  legend: {
    show: true,
    position: "top",
  },
};

const timeSpentData: ApexOptions = {
  series: [
    {
      name: "Spent Time",
      type: "column",
      data: [35, 45, 32, 40, 35, 38, 40],
    },
    {
      name: "Total Time",
      type: "line",
      data: [30, 25, 36, 30, 40, 35],
    },
  ],
  chart: {
    height: 280,
    type: "line",
    stacked: false,
  },
  annotations: {
    points: [
      {
        x: "S",
        y: 35,
        marker: {
          size: 5,
          strokeColor: "rgba(var(--warning),1)",
          strokeWidth: 4,
          cssClass: "marker-warning",
        },
      },
    ],
  },
  stroke: {
    width: [0, 2, 5],
    curve: "smooth",
  },
  plotOptions: {
    bar: {
      columnWidth: "26",
    },
  },
  legend: {
    show: false,
  },
  colors: ["rgba(var(--warning),1)"],
  fill: {
    type: ["gradient", "solid"],
    opacity: [0.8, 0.1],
    gradient: {
      inverseColors: false,
      shade: "light",
      type: "vertical",
      opacityFrom: 0.1,
      opacityTo: 0.1,
      colorStops: [
        {
          offset: 0,
          color: "rgba(var(--primary),.1)",
          opacity: 1,
        },
        {
          offset: 50,
          color: "rgba(var(--primary),.1)",
          opacity: 1,
        },
        {
          offset: 100,
          color: "rgba(var(--primary),.1)",
          opacity: 1,
        },
      ],
    },
  },
  markers: {
    size: 0,
  },
  xaxis: {
    type: "category",
    categories: ["M", "T", "W", "T", "F", "S", "S"],
    tooltip: {
      enabled: false,
    },
    axisBorder: {
      show: false,
    },
  },
  yaxis: {
    show: false,
  },
  grid: {
    show: false,
    xaxis: {
      lines: {
        show: false,
      },
    },
    yaxis: {
      lines: {
        show: false,
      },
    },
  },
  tooltip: {
    x: {
      show: false,
    },
    style: {
      fontSize: "16px",
      fontFamily: '"Outfit", sans-serif',
    },
  },
  // responsive: [{
  //   breakpoint: 1440,
  //   options: {
  //     chart: {
  //       height: 200
  //     },
  //   }
  // }]
};

const ApiRequestData: ApexOptions = {
  series: [
    {
      name: "",
      data: [
        19.0, 20, 19.8, 19, 19.67, 19.45, 20.99, 30.45, 19.45, 19.09, 19.8,
        19.6, 20, 20.89, 21.4, 19.09, 20.8, 23.78, 25.0, 20, 19.65, 25, 24.89,
        23, 19.0, 19.56, 20.36, 22.9, 24.9, 19.78,
      ],
    },
  ],
  chart: {
    type: "area",
    height: 350,
    offsetY: 0,
    offsetX: 0,
    toolbar: {
      show: false,
    },
  },
  stroke: {
    width: 2,
    curve: "smooth",
  },
  dataLabels: {
    enabled: false,
  },
  fill: {
    type: "gradient",
    gradient: {
      shadeIntensity: 1,
      colorStops: [
        {
          offset: 0,
          color: "rgba(var(--info),.4)",
          opacity: 1,
        },
        {
          offset: 50,
          color: "rgba(var(--info),.4)",
          opacity: 1,
        },
        {
          offset: 100,
          color: "rgba(var(--info),.1)",
          opacity: 1,
        },
      ],
    },
  },
  legend: {
    show: false,
  },
  colors: ["rgba(var(--info))"],
  xaxis: {
    tooltip: {
      enabled: false,
    },
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
  },
  tooltip: {
    x: {
      show: false,
    },
    style: {
      fontSize: "16px",
      fontFamily: '"Outfit", sans-serif',
    },
  },
  responsive: [
    {
      breakpoint: 1660,
      options: {
        chart: {
          height: 365,
        },
      },
    },
  ],
};

const ProjectChartData: ApexOptions = {
  series: [
    {
      name: "Income",
      data: [35, 35, 18, 45, 45, 10, 20],
    },
    {
      name: "Expense",
      data: [10, 25, 15, 25, 20, 45],
    },
  ],
  chart: {
    height: 240,
    type: "line",
    dropShadow: {
      enabled: true,
      top: 0,
      left: 0,
      blur: 1,
      color: "rgba(var(--primary),1)",
      opacity: 0.6,
    },
  },

  colors: ["rgba(var(--primary),1)", "rgba(var(--success),1)"],
  dataLabels: {
    enabled: false,
  },

  stroke: {
    width: 2,
    curve: "smooth",
    dashArray: [0, 2],
  },
  annotations: {
    points: [
      {
        x: "Jun",
        y: 45,
        marker: {
          size: 5,
          strokeColor: "rgba(var(--success),1)",
          strokeWidth: 4,
          cssClass: "marker-success",
        },
      },
      {
        x: "Jun",
        y: 10,
        marker: {
          size: 5,
          strokeColor: "rgba(var(--primary),1)",
          strokeWidth: 4,
          cssClass: "marker-primary",
        },
      },
    ],
  },
  xaxis: {
    categories: [
      "Jan",
      "Feb",
      "Mar",
      "Apr",
      "May",
      "Jun",
      "July",
      "Aug",
      "Sep",
      "Oct",
      "Nov",
      "Dec",
    ],
    labels: {
      show: true,
      style: {
        colors: [],
        fontSize: "14px",
        fontFamily: '"Montserrat", system-ui',
        fontWeight: 600,
      },
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
    tooltip: {
      enabled: false,
    },
  },
  grid: {
    show: true,
    borderColor: "rgba(var(--dark),.2)",
    xaxis: {
      lines: {
        show: false,
      },
    },
    yaxis: {
      lines: {
        show: true,
      },
    },
  },
  yaxis: {
    show: true,
    labels: {
      formatter: function (value) {
        return value + "$";
      },
      style: {
        colors: [],
        fontSize: "14px",
        fontFamily: '"Montserrat", system-ui',
        fontWeight: 600,
      },
    },
  },
  legend: {
    show: false,
  },
  tooltip: {
    x: {
      show: false,
    },
    style: {
      fontSize: "16px",
      fontFamily: '"Outfit", sans-serif',
    },
  },
  responsive: [
    {
      breakpoint: 1399,
      options: {
        chart: {
          height: 220,
        },
      },
    },
    {
      breakpoint: 567,
      options: {
        yaxis: {
          show: false,
        },
      },
    },
  ],
};

const EcommerceChartData: ApexOptions = {
  series: [
    {
      name: "",
      data: [
        15.0, 20, 15.8, 20.8, 15, 15.67, 15.45, 20.89, 20.45, 15.45, 15.09,
        15.8, 15.6, 20, 20.89, 21.4, 15.09, 20.8, 23.78, 25.0, 20, 15.65, 25,
        24.89, 23, 15.0, 15.56, 20.36, 22.9, 24.9, 15.78,
      ],
    },
  ],
  chart: {
    fontFamily: 'Montserrat", system-ui',
    type: "line",
    height: 75,
    offsetY: 0,
    offsetX: 0,
    toolbar: {
      show: false,
    },
  },
  stroke: {
    width: 2,
    curve: "smooth",
  },
  dataLabels: {
    enabled: false,
  },
  fill: {
    type: "gradient",
    gradient: {
      shadeIntensity: 1,
      colorStops: [
        {
          offset: 0,
          color: "rgba(var(--danger-dark),1)",
          opacity: 1,
        },
        {
          offset: 50,
          color: "rgba(var(--danger-dark),.6)",
          opacity: 1,
        },
        {
          offset: 100,
          color: "rgba(var(--danger-dark),.4)",
          opacity: 1,
        },
      ],
    },
  },
  legend: {
    show: false,
  },
  colors: ["rgba(var(--danger))"],
  xaxis: {
    labels: {
      show: false,
    },
    axisBorder: {
      show: false,
    },
    axisTicks: {
      show: false,
    },
    tooltip: {
      enabled: false,
    },
  },
  yaxis: {
    min: 15,
    max: 25,
    labels: {
      show: false,
    },
  },
  grid: {
    show: false,
    padding: {
      top: -10,
      right: 0,
      bottom: -15,
      left: 0,
    },
  },
  tooltip: {
    x: {
      show: false,
    },
    style: {
      fontSize: "16px",
      fontFamily: '"Outfit", sans-serif',
    },
  },
};
const EcommerceOrderData: ApexOptions = {
  series: [
    {
      name: "Calories Burned",
      data: [150, 220, 350, 180, 270, 160],
    },
  ],
  chart: {
    fontFamily: 'Montserrat", system-ui',
    type: "bar",
    height: 324,
    toolbar: {
      show: false,
    },
  },
  plotOptions: {
    bar: {
      borderRadius: 15, // Rounds the bars
      columnWidth: "60",
      distributed: true, // Different colors for each bar
      // endingShape: "rounded",
    },
  },
  colors: [
    "rgba(var(--primary),1)",
    "rgba(var(--primary),.3)",
    "rgba(var(--primary),1)",
    "rgba(var(--primary),1)",
    "rgba(var(--danger),.3)",
    "rgba(var(--danger-dark),1)",
  ], // Custom colors for each bar
  dataLabels: {
    enabled: false,
  },
  legend: {
    show: false,
  },
  xaxis: {
    categories: ["26 Feb", "29 Feb", "1 Mar", "2 Mar", "3 Mar", "4 Mar"],
    labels: {
      offsetX: 0,
      offsetY: 0,
      style: {
        fontSize: "14px",
        fontWeight: 600,
        colors: "rgba(var(--dark),1)",
      },
    },
    offsetX: 0,
    offsetY: 0,
    axisTicks: {
      offsetX: 0,
      offsetY: 0,
      show: false,
    },
    axisBorder: {
      offsetX: 0,
      offsetY: 0,
      show: false,
    },
  },
  yaxis: {
    show: false,
  },
  grid: {
    show: false,
  },
  tooltip: {
    custom: function ({ series, seriesIndex, dataPointIndex, w }) {
      // Custom tooltip content
      const data = series[seriesIndex][dataPointIndex];
      const category = w.config.xaxis.categories[dataPointIndex];
      return (
        '<div class="arrow_box p-2">' +
        "<span>" +
        category +
        "</span>" +
        '<div style="font-weight: bold; font-size: 14px;">Weightlifting</div>' +
        "<span>" +
        data +
        " kcal</span>" +
        "</div>"
      );
    },
    style: {
      fontSize: "16px",
    },
  },

  fill: {
    type: ["", "", "image", "", "", "image"],
    image: {
      src: [
        "",
        "",
        "/images/dashboard/ecommerce-dashboard/01.png",
        "",
        "",
        "/images/dashboard/ecommerce-dashboard/03.png",
      ],
      width: 400,
      height: 400,
    },
  },
  responsive: [
    {
      breakpoint: 1399,
      options: {
        chart: {
          height: 304,
        },
      },
    },
  ],
};
const EcommerceSaleReport: ApexOptions = {
  series: [44, 55, 41, 17, 15],
  chart: {
    fontFamily: "Montserrat, system-ui",
    type: "donut",
    dropShadow: {
      enabled: false,
      color: "#111",
      top: -1,
      left: 3,
      blur: 3,
      opacity: 0.2,
    },
  },
  stroke: {
    width: 0,
  },
  legend: {
    position: "bottom",
    fontSize: "14px",
    fontWeight: 500,
    labels: {
      useSeriesColors: true,
    },
    markers: {
      size: 6,
      shape: "circle",
      strokeWidth: 0,
    },
  },
  plotOptions: {
    pie: {
      donut: {
        labels: {
          show: false,
          total: {
            showAlways: false,
            show: false,
          },
        },
      },
    },
  },
  labels: ["Comedy", "Action", "SciFi", "Drama", "Horror"],
  dataLabels: {
    enabled: false,
    dropShadow: {
      blur: 3,
      opacity: 0.8,
    },
  },
  colors: [
    "rgba(var(--primary-dark),1)",
    "rgba(var(--primary),1)",
    "rgba(var(--danger-dark),1)",
    "rgba(var(--danger),.3)",
    "rgba(var(--warning),1)",
  ],
  fill: {
    type: ["pattern", "solid", "pattern", "solid", "solid"],
    opacity: 1,
    pattern: {
      style: [
        "verticalLines",
        "horizontalLines",
        "horizontalLines",
        "circles",
        "horizontalLines",
      ],
    },
  },
  states: {
    hover: {
      filter: { type: "none" },
    },
  },
  theme: {
    palette: "palette2",
  },
  tooltip: {
    x: {
      show: false,
    },
    style: {
      fontSize: "16px",
    },
  },
};
const EcommerceOverviewReport: ApexOptions = {
  series: [
    {
      name: "data 1",
      data: [3.2, 4, 2.15, 3, 2.4, 2, 1.2, 4, 2.1, 1],
    },
    {
      name: "data 1",
      data: [-2.25, -3, -2.5, -1, -2.4, -1.5, -2.2, -3, -2.65, -2],
    },
    {
      name: "data 2",
      data: [-2.25, -3, -2.5, -1, -2.4, -1.5, -2.2, -3, -2.65, -2],
    },
    {
      name: "data 3",
      data: [-3.25, -4, -3.5, -2, -2.4, -1.5, -2.2, -3, -2.65, -2],
    },

    {
      name: "data 4",
      data: [-2.25, -3, -2.5, -1, -2.4, -1.5, -2.2, -3, -2.65, -2],
    },
    {
      name: "data 5",
      data: [-2.25, -3, -2.5, -1, -2.4, -1.5, -2.2, -3, -2.65, -2],
    },
  ],

  chart: {
    fontFamily: 'Montserrat", system-ui',
    type: "bar",
    height: 240,
    stacked: true,
    toolbar: {
      show: false,
    },
  },
  plotOptions: {
    bar: {
      columnWidth: "10",
      borderRadius: 2,
    },
  },
  colors: [
    "rgba(var(--warning),.3)",
    "rgba(var(--danger-dark),1)",
    "rgba(var(--danger),.3)",
    "rgba(var(--warning-dark),1)",
  ],
  dataLabels: {
    enabled: false,
  },
  xaxis: {
    categories: [
      "Jan",
      "Feb",
      "Mar",
      "Apr",
      "May",
      "Jun",
      "July",
      "Aug",
      "Sep",
      "Oct",
      "Nov",
      "Dec",
    ],
    offsetX: 0,
    offsetY: 0,
    labels: {
      offsetX: 0,
      offsetY: 0,
      show: false,
    },
    axisBorder: {
      offsetX: 0,
      offsetY: 0,
      show: false,
    },
    axisTicks: {
      offsetX: 0,
      offsetY: 0,
      show: false,
    },
  },
  yaxis: {
    show: false,
  },
  grid: {
    show: false,
    xaxis: {
      lines: {
        show: false,
      },
    },
    yaxis: {
      lines: {
        show: false,
      },
    },
  },
  legend: {
    show: false,
  },
  tooltip: {
    enabled: false,
  },
};

export {
  chartOptions1,
  chartOptions2,
  chartOptions3,
  chartOptions4,
  chartOptions5,
  fileManagerOption,
  timeSpentData,
  ApiRequestData,
  ProjectChartData,
  EcommerceChartData,
  EcommerceOrderData,
  EcommerceSaleReport,
  EcommerceOverviewReport,
};
