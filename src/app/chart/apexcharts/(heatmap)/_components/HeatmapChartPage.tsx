"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import { ApexChartsData } from "@/Data/Charts/ApexCharts";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const HeatmapChartPage = () => {
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
          mainTitle="Heatmap"
          title="Chart"
          path={["Apexcharts", "Heatmap"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col xl="6">
            <div className="card equal-card">
              <CardHeader>
                <h5> Basic Heatmap – Single Series </h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.HeatMapSingleSeries}
                    type={"heatmap"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "heatmap",
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      colors: [
                        getLocalStorageItem("color-primary", "#8973EA"),
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#147534",
                        "#E90BC4",
                        "#EAEA4F",
                        "#2E5CE2",
                        "#D1CAC4",
                        "#282632",
                      ],
                      title: {
                        text: "",
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
            </div>
          </Col>
          <Col xl="6">
            <Card>
              <CardHeader>
                <h5> Heatmap – Multiple Series </h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.HeatMapMultipleSeries}
                    type={"heatmap"}
                    height={450}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 450,
                        type: "heatmap",
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      xaxis: {
                        type: "category",
                        categories: [
                          "10:00",
                          "10:30",
                          "11:00",
                          "11:30",
                          "12:00",
                          "12:30",
                          "01:00",
                          "01:30",
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

                      title: {
                        text: "",
                      },
                      grid: {
                        padding: {
                          right: 20,
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
          <Col xl="6">
            <Card>
              <CardHeader>
                <h5> Heatmap – Color Range</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.HeatMapColorRange}
                    type={"heatmap"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "heatmap",
                      },
                      plotOptions: {
                        heatmap: {
                          shadeIntensity: 0.5,
                          radius: 0,
                          useFillColorAsStroke: true,
                          colorScale: {
                            ranges: [
                              {
                                from: -30,
                                to: 5,
                                name: "low",
                                color: getLocalStorageItem(
                                  "color-primary",
                                  "#8973ea"
                                ),
                              },
                              {
                                from: 6,
                                to: 20,
                                name: "medium",
                                color: getLocalStorageItem(
                                  "color-secondary",
                                  "#626263"
                                ),
                              },
                              {
                                from: 21,
                                to: 45,
                                name: "high",
                                color: "#147534",
                              },
                              {
                                from: 46,
                                to: 55,
                                name: "extreme",
                                color: "#e90bc4",
                              },
                            ],
                          },
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        width: 1,
                      },
                      title: {
                        text: "",
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
          <Col xl="6">
            <Card>
              <CardHeader>
                <h5> Heatmap – Range without Shades</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.HeatMapRangeShades}
                    type={"heatmap"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "heatmap",
                      },
                      stroke: {
                        width: 0,
                      },
                      plotOptions: {
                        heatmap: {
                          radius: 30,
                          enableShades: false,
                          colorScale: {
                            ranges: [
                              {
                                from: 0,
                                to: 50,
                                color: "#F9D249",
                              },
                              {
                                from: 51,
                                to: 100,
                                color: "#535AE7",
                              },
                            ],
                          },
                        },
                      },
                      dataLabels: {
                        enabled: true,
                        style: {
                          colors: ["#fff"],
                        },
                      },
                      xaxis: {
                        type: "category",
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

export default HeatmapChartPage;
