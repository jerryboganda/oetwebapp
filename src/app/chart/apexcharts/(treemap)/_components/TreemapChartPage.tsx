"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { ApexChartsData } from "@/Data/Charts/ApexCharts";
import { getLocalStorageItem } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const TreemapChartPage = () => {
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
          mainTitle="Treemap"
          title="Chart"
          path={["Apexcharts", "Treemap"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col sm="12">
            <Card>
              <CardHeader>
                <h5> Basic Treemap Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.BasicTreemapData}
                    type={"treemap"}
                    height={350}
                    options={{
                      legend: {
                        show: false,
                      },
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "treemap",
                      },
                      colors: [getLocalStorageItem("color-primary", "#8973ea")],
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
          <Col sm="12">
            <Card>
              <CardHeader>
                <h5> Multi-Dimensional Treemap Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.MultiDimensionalTreemap}
                    type={"treemap"}
                    height={350}
                    options={{
                      legend: {
                        show: false,
                      },
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "treemap",
                      },
                      colors: [
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#147534",
                      ],

                      title: {
                        text: "",
                        align: "center",
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
          <Col sm="12">
            <Card>
              <CardHeader>
                <h5> Distributed Treemap Chart (set color for each cell)</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.DistributedTreemapData}
                    type={"treemap"}
                    height={350}
                    options={{
                      legend: {
                        show: false,
                      },
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "treemap",
                      },
                      title: {
                        text: "",
                        align: "center",
                      },
                      colors: [
                        getLocalStorageItem("color-primary", "#8973ea"),
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#147534",
                        "#e90bc4",
                        "#eaea4f",
                        "#2e5ce2",
                        "#d1cac4",
                        "#282632",
                      ],
                      plotOptions: {
                        treemap: {
                          distributed: true,
                          enableShades: false,
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
          <Col sm="12">
            <Card>
              <CardHeader>
                <h5> Treemap Chart with Color ranges</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.ColorRangeTreemap}
                    type={"treemap"}
                    height={350}
                    options={{
                      legend: {
                        show: false,
                      },
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "treemap",
                      },
                      title: {
                        text: "",
                      },
                      dataLabels: {
                        enabled: true,
                        style: {
                          fontSize: "12px",
                        },
                        formatter: function (
                          text: string,
                          op: { value: number }
                        ) {
                          return [text, op.value];
                        },
                        offsetY: -4,
                      },
                      plotOptions: {
                        treemap: {
                          enableShades: true,
                          shadeIntensity: 0.5,
                          reverseNegativeShade: true,
                          colorScale: {
                            ranges: [
                              {
                                from: -6,
                                to: 0,
                                color: "#282632",
                              },
                              {
                                from: 0.001,
                                to: 6,
                                color: getLocalStorageItem(
                                  "color-primary",
                                  "#8973ea"
                                ),
                              },
                            ],
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

export default TreemapChartPage;
