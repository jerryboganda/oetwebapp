"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const LinePage = () => {
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
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Line"
          title="Chart"
          path={["Apexcharts", "Line"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Basic Line Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Desktops",
                        data: [10, 41, 35, 51, 49, 62, 69, 91, 148],
                      },
                    ]}
                    type={"line"}
                    options={{
                      chart: {
                        fontFamily: 'Montserrat", system-ui',
                        height: 300,
                        type: "line",
                        zoom: {
                          enabled: false,
                        },
                      },
                      stroke: {
                        curve: "smooth",
                      },
                      title: {
                        text: "",
                        align: "left",
                      },
                      colors: [getLocalStorageItem("color-primary", "#8973EA")],
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
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Gradient line chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Sales",
                        data: [
                          4, 3, 10, 9, 29, 19, 22, 9, 12, 7, 19, 5, 13, 9, 17,
                          2, 7, 5,
                        ],
                      },
                    ]}
                    type={"line"}
                    options={{
                      chart: {
                        fontFamily: 'Montserrat", system-ui',
                        height: 350,
                      },
                      forecastDataPoints: {
                        count: 7,
                      },
                      stroke: {
                        width: 5,
                        curve: "smooth",
                      },
                      xaxis: {
                        type: "datetime",
                        categories: [
                          "1/11/2000",
                          "2/11/2000",
                          "3/11/2000",
                          "4/11/2000",
                          "5/11/2000",
                          "6/11/2000",
                          "7/11/2000",
                          "8/11/2000",
                          "9/11/2000",
                          "10/11/2000",
                          "11/11/2000",
                          "12/11/2000",
                          "1/11/2001",
                          "2/11/2001",
                          "3/11/2001",
                          "4/11/2001",
                          "5/11/2001",
                          "6/11/2001",
                        ],
                        tickAmount: 10,
                        labels: {
                          formatter: function (
                            _value: string | number,
                            timestamp: number,
                            opts: {
                              dateFormatter: (
                                date: Date,
                                format: string
                              ) => string;
                            }
                          ): string {
                            return opts.dateFormatter(
                              new Date(timestamp),
                              "dd MMM"
                            );
                          },

                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      title: {
                        text: "",
                        align: "left",
                        style: {
                          fontSize: "16px",
                          color: "#f6c355",
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
                      fill: {
                        type: "gradient",
                        gradient: {
                          shade: "#26C450",
                          shadeIntensity: 1,
                          type: "horizontal",
                          opacityFrom: 1,
                          opacityTo: 1,
                          colorStops: [
                            {
                              offset: 0,
                              color: "rgba(var(--primary),1)",
                              opacity: 1,
                            },
                            {
                              offset: 50,
                              color: "rgba(var(--success),1)",
                              opacity: 1,
                            },
                            {
                              offset: 100,
                              color: "rgba(var(--warning),1)",
                              opacity: 0.1,
                            },
                          ],
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
          <Col md="6">
            <Card>
              <CardHeader>
                <h5> Dashed Line Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Session Duration",
                        data: [45, 52, 38, 24, 33, 26, 21, 20, 6, 8, 15, 10],
                      },
                      {
                        name: "Page Views",
                        data: [35, 41, 62, 42, 13, 18, 29, 37, 36, 51, 32, 35],
                      },
                      {
                        name: "Total Visits",
                        data: [87, 57, 74, 99, 75, 38, 62, 47, 82, 56, 45, 47],
                      },
                    ]}
                    type={"line"}
                    options={{
                      chart: {
                        fontFamily: 'Montserrat", system-ui',
                        height: 350,

                        zoom: {
                          enabled: false,
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        width: [5, 7, 5],
                        curve: "straight",
                        dashArray: [0, 8, 5],
                      },
                      title: {
                        text: "",
                        align: "left",
                      },
                      legend: {
                        tooltipHoverFormatter: function (
                          val: string,
                          opts: {
                            w?: {
                              globals?: {
                                series?: number[][];
                              };
                            };
                            seriesIndex: number;
                            dataPointIndex: number;
                          }
                        ): string {
                          const seriesValue =
                            opts.w?.globals?.series?.[opts.seriesIndex]?.[
                              opts.dataPointIndex
                            ] ?? "N/A";

                          return `${val} - ${seriesValue}`;
                        },
                      },
                      markers: {
                        size: 0,
                        hover: {
                          sizeOffset: 6,
                        },
                      },

                      colors: [
                        "rgba(var(--success),1)",
                        "rgba(var(--info),1)",
                        "rgba(var(--danger),1)",
                      ],
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
                        style: {
                          fontSize: "14px",
                        },
                        y: [
                          {
                            title: {
                              formatter: function (val: number) {
                                return val + " (mins)";
                              },
                            },
                          },
                          {
                            title: {
                              formatter: function (val: number) {
                                return val + " per session";
                              },
                            },
                          },
                          {
                            title: {
                              formatter: function (val: number) {
                                return val;
                              },
                            },
                          },
                        ],
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
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Stepline Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Sales",
                        data: [
                          4, 3, 10, 9, 29, 19, 22, 9, 12, 7, 19, 5, 13, 9, 17,
                          2, 7, 5,
                        ],
                      },
                    ]}
                    type={"line"}
                    options={{
                      stroke: {
                        curve: "stepline",
                      },
                      dataLabels: {
                        enabled: false,
                      },

                      colors: ["#E90BC4"],
                      title: {
                        text: "",
                        align: "left",
                      },
                      markers: {
                        hover: {
                          sizeOffset: 4,
                        },
                      },
                      chart: {
                        fontFamily: 'Montserrat", system-ui',
                        height: 350,
                        type: "line",

                        toolbar: {
                          show: false,
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
                      yaxis: {
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      xaxis: {
                        labels: {
                          style: {
                            colors: [],
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

export default LinePage;
