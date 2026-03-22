"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { generateDayWiseTimeSeries } from "@/_helper";
import { ApexChartsData } from "@/Data/Charts/ApexCharts";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const AreaChartPage = () => {
  const [ApexCharts, setApexCharts] = useState<any>(null);
  useEffect(() => {
    const loadModules = async () => {
      const [chartModule] = await Promise.all([import("react-apexcharts")]);

      setApexCharts(() => chartModule.default || chartModule);
    };

    if (typeof window !== "undefined") {
      loadModules();
    }
  }, []);

  let ts1 = 1388534400000;
  let ts2 = 1388620800000;
  let ts3 = 1389052800000;

  // Initialize dataSet with proper type and default empty arrays
  const dataSet: [number, number][][] = [
    [] as [number, number][],
    [] as [number, number][],
    [] as [number, number][],
  ];

  // Helper function to safely push to dataSet
  const pushToDataSet = (index: number, value: [number, number]) => {
    if (dataSet[index] && Array.isArray(dataSet[index])) {
      dataSet[index].push(value);
    }
  };

  // First dataset (using dataSeries[2])
  for (let i = 0; i < 12; i++) {
    ts1 = ts1 + 86400000;
    const dataPoint = ApexChartsData.dataSeries[2]?.[i];
    if (dataPoint) {
      const value =
        typeof dataPoint === "object" && "value" in dataPoint
          ? dataPoint.value
          : 0;
      const innerArr1: [number, number] = [ts1, value];
      pushToDataSet(0, innerArr1);
    }
  }

  // Second dataset (using dataSeries[1])
  for (let i = 0; i < 18; i++) {
    ts2 = ts2 + 86400000;
    const dataPoint = ApexChartsData.dataSeries[1]?.[i];
    if (dataPoint) {
      const value =
        typeof dataPoint === "object" && "value" in dataPoint
          ? dataPoint.value
          : 0;
      const innerArr2: [number, number] = [ts2, value];
      pushToDataSet(1, innerArr2);
    }
  }

  // Third dataset (using dataSeries[0])
  for (let i = 0; i < 12; i++) {
    ts3 = ts3 + 86400000;
    const dataPoint = ApexChartsData.dataSeries[0]?.[i];
    if (dataPoint) {
      const value =
        typeof dataPoint === "object" && "value" in dataPoint
          ? dataPoint.value
          : 0;
      const innerArr3: [number, number] = [ts3, value];
      pushToDataSet(2, innerArr3);
    }
  }

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Area"
          title="Chart"
          path={["Apexcharts", "Area"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Basic Area chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "STOCK ABC",
                        data: [10, 51, 35, 51, 59, 62, 79, 91, 148],
                      },
                    ]}
                    type={"area"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: 'Montserrat", system-ui',
                        zoom: {
                          enabled: false,
                        },
                      },
                      colors: ["rgba(var(--primary),1)"],
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        curve: "smooth",
                      },
                      fill: {
                        type: "gradient",
                        gradient: {
                          shadeIntensity: 1,
                          colorStops: [
                            {
                              offset: 0,
                              color: "rgba(var(--primary),1)",
                              opacity: 1,
                            },
                            {
                              offset: 50,
                              color: "rgba(var(--primary),1)",
                              opacity: 1,
                            },
                            {
                              offset: 100,
                              color: "rgba(var(--primary),.1)",
                              opacity: 0.1,
                            },
                          ],
                        },
                      },
                      xaxis: {
                        categories: [
                          "Jan",
                          "Feb",
                          "Mar",
                          "Apr",
                          "May",
                          "Jun",
                          "Jul",
                          "Aug",
                          "Sep",
                        ],
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      yaxis: {
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
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
                      tooltip: {
                        x: {
                          show: false,
                        },
                        style: {
                          fontSize: "16px",
                        },
                      },
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5> Spline Area</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "series1",
                        data: [31, 40, 28, 51, 42, 109, 100],
                      },
                      {
                        name: "series2",
                        data: [11, 32, 45, 32, 34, 52, 41],
                      },
                    ]}
                    type={"area"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "area",
                      },
                      colors: ["#eaea4f", "#147534"], // Different colors for each series
                      fill: {
                        type: "gradient",
                        gradient: {
                          shadeIntensity: 1,
                          opacityFrom: 1,
                          opacityTo: 0,
                          stops: [0, 100], // Stops for the gradient
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        curve: "smooth",
                      },
                      xaxis: {
                        type: "datetime",
                        categories: [
                          "2018-09-19T00:00:00.000Z",
                          "2018-09-19T01:30:00.000Z",
                          "2018-09-19T02:30:00.000Z",
                          "2018-09-19T03:30:00.000Z",
                          "2018-09-19T04:30:00.000Z",
                          "2018-09-19T05:30:00.000Z",
                          "2018-09-19T06:30:00.000Z",
                        ],
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      yaxis: {
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      tooltip: {
                        x: {
                          format: "dd/MM/yy HH:mm",
                        },
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
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
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5> Irregular TimeSeries</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "PRODUCT A",
                        data: dataSet[0] || [],
                      },
                      {
                        name: "PRODUCT B",
                        data: dataSet[1] || [],
                      },
                      {
                        name: "PRODUCT C",
                        data: dataSet[2] || [],
                      },
                    ]}
                    type={"area"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "area",
                        stacked: false,
                        height: 350,
                        zoom: {
                          enabled: false,
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      markers: {
                        size: 0,
                      },
                      colors: ["#2e5ce2", "#147534", "#e90bc4"],
                      fill: {
                        type: "gradient",
                        gradient: {
                          shadeIntensity: 1,
                          inverseColors: false,
                          opacityFrom: 0.45,
                          opacityTo: 0.05,
                          stops: [20, 100, 100, 100],
                        },
                      },
                      yaxis: {
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                          offsetX: 0,
                          formatter: function (val: number) {
                            return (val / 1000000).toFixed(2); // Format as millions
                          },
                        },
                        axisBorder: {
                          show: false,
                        },
                        axisTicks: {
                          show: false,
                        },
                      },
                      xaxis: {
                        type: "datetime",
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },

                      tooltip: {
                        x: {
                          show: false,
                        },
                        style: {
                          fontSize: "16px",
                        },
                      },
                      legend: {
                        position: "top",
                        horizontalAlign: "right",
                        offsetX: -10,
                      },
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Stacked Area</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "South",
                        data: generateDayWiseTimeSeries(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "North",
                        data: generateDayWiseTimeSeries(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 20,
                          }
                        ),
                      },
                      {
                        name: "Central",
                        data: generateDayWiseTimeSeries(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 15,
                          }
                        ),
                      },
                    ]}
                    type={"area"}
                    height={350}
                    options={{
                      chart: {
                        type: "area",
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        animations: {
                          enabled: false,
                        },
                        zoom: {
                          enabled: false,
                        },
                      },

                      colors: ["rgba(var(--secondary))"],
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        curve: "straight",
                      },
                      fill: {
                        opacity: 0.8,
                        type: "pattern",
                        pattern: {
                          style: ["verticalLines", "horizontalLines"],
                          width: 5,
                          height: 6,
                        },
                      },
                      markers: {
                        size: 5,
                        hover: {
                          size: 9,
                        },
                      },
                      tooltip: {
                        x: {
                          show: false,
                        },
                        style: {
                          fontSize: "16px",
                        },
                      },
                      theme: {
                        palette: "palette1",
                      },
                      xaxis: {
                        type: "datetime",
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      yaxis: {
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default AreaChartPage;
