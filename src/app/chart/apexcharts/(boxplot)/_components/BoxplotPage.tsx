"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const BoxplotPage = () => {
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
  const adTypes: Record<number, string> = {
    2017: "Banner",
    2018: "Video",
    2019: "Native",
    2020: "Social",
    2021: "Sponsored",
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Boxplot"
          title="Chart"
          path={["Apexcharts", "Boxplot"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5> Basic Box & Whisker Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        type: "boxPlot",
                        data: [
                          {
                            x: "Jan 2015",
                            y: [54, 66, 69, 75, 88],
                          },
                          {
                            x: "Jan 2016",
                            y: [43, 65, 69, 76, 81],
                          },
                          {
                            x: "Jan 2017",
                            y: [31, 39, 45, 51, 59],
                          },
                          {
                            x: "Jan 2018",
                            y: [39, 46, 55, 65, 71],
                          },
                          {
                            x: "Jan 2019",
                            y: [29, 31, 35, 39, 44],
                          },
                          {
                            x: "Jan 2020",
                            y: [41, 49, 58, 61, 67],
                          },
                          {
                            x: "Jan 2021",
                            y: [54, 59, 66, 71, 88],
                          },
                        ],
                      },
                    ]}
                    height={350}
                    type="boxPlot"
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "boxPlot",
                        height: 350,
                      },
                      title: {
                        text: "",
                        align: "left",
                      },

                      plotOptions: {
                        boxPlot: {
                          colors: {
                            upper:
                              getLocalStorageItem("color-primary", "#8973ea") ||
                              "#8973ea",
                            lower:
                              getLocalStorageItem(
                                "color-secondary",
                                "#626263"
                              ) || "#626263",
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
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5> BoxPlot with Scatter Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "box",
                        type: "boxPlot",
                        data: [
                          {
                            x: new Date("2017-01-01").getTime(),
                            y: [54, 66, 69, 75, 88],
                          },
                          {
                            x: new Date("2018-01-01").getTime(),
                            y: [43, 65, 69, 76, 81],
                          },
                          {
                            x: new Date("2019-01-01").getTime(),
                            y: [31, 39, 45, 51, 59],
                          },
                          {
                            x: new Date("2020-01-01").getTime(),
                            y: [39, 46, 55, 65, 71],
                          },
                          {
                            x: new Date("2021-01-01").getTime(),
                            y: [29, 31, 35, 39, 44],
                          },
                        ],
                      },
                      {
                        name: "outliers",
                        type: "scatter",
                        data: [
                          {
                            x: new Date("2017-01-01").getTime(),
                            y: 32,
                          },
                          {
                            x: new Date("2018-01-01").getTime(),
                            y: 25,
                          },
                          {
                            x: new Date("2019-01-01").getTime(),
                            y: 64,
                          },
                          {
                            x: new Date("2020-01-01").getTime(),
                            y: 27,
                          },
                          {
                            x: new Date("2021-01-01").getTime(),
                            y: 15,
                          },
                        ],
                      },
                    ]}
                    height={350}
                    type="boxPlot"
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "boxPlot",
                        height: 350,
                      },
                      title: {
                        text: "",
                        align: "left",
                      },
                      xaxis: {
                        type: "datetime",
                        tooltip: {
                          formatter: function (val: number) {
                            const year = new Date(val).getFullYear();
                            return `${year} - ${adTypes[year] || "Unknown"}`;
                          },
                        },
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
                      plotOptions: {
                        boxPlot: {
                          colors: {
                            upper: "#147534",
                            lower: "#e90bc4",
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
                        shared: false,
                        intersect: true,

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
        </Row>
      </Container>
    </div>
  );
};

export default BoxplotPage;
