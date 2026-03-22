"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const PieChartPage = () => {
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
          mainTitle="pie"
          title="Chart"
          path={["Apexcharts", "pie"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col lg="6" xl="4">
            <Card>
              <CardHeader>
                <h5> Simple Pie Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[44, 55, 13, 43, 22]}
                    type={"pie"}
                    height={340}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 340,
                        type: "pie",
                      },
                      tooltip: {
                        x: {
                          show: false,
                        },
                        style: {
                          fontSize: "16px",
                        },
                      },
                      colors: [
                        getLocalStorageItem("color-primary", "#8973ea"),
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#147534",
                        "#e90bc4",
                        "#eaea4f",
                      ],
                      labels: [
                        "Team A",
                        "Team B",
                        "Team C",
                        "Team D",
                        "Team E",
                      ],
                      legend: {
                        position: "bottom",
                      },
                      responsive: [
                        {
                          breakpoint: 1366,
                          options: {
                            chart: {
                              height: 250,
                            },
                            legend: {
                              show: false,
                            },
                          },
                        },
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xl="4">
            <Card>
              <CardHeader>
                <h5>Simple Donut Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[44, 55, 41, 17, 15]}
                    type={"donut"}
                    height={340}
                    options={{
                      chart: {
                        type: "donut",
                        height: 340,
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      markers: {
                        colors: [
                          getLocalStorageItem("color-primary", "#48BECE"),
                          getLocalStorageItem("color-secondary", "#8B8476"),
                          "#AECC34",
                          "#FF5E40",
                          "#F9D249",
                          "#535AE7",
                          "#E5E3E0",
                          "#48443D",
                        ],
                      },
                      fill: {
                        colors: [
                          getLocalStorageItem("color-primary", "#48BECE"),
                          getLocalStorageItem("color-secondary", "#8B8476"),
                          "#AECC34",
                          "#FF5E40",
                          "#F9D249",
                          "#535AE7",
                          "#E5E3E0",
                          "#48443D",
                        ],
                      },
                      labels: ["Device 1", "Device 2", "Device 3", "Device 4"],

                      colors: [
                        getLocalStorageItem("color-primary", "#48BECE"),
                        getLocalStorageItem("color-secondary", "#8B8476"),
                        "#AECC34",
                        "#FF5E40",
                        "#F9D249",
                        "#535AE7",
                        "#E5E3E0",
                        "#48443D",
                      ],
                      responsive: [
                        {
                          breakpoint: 1366,
                          options: {
                            chart: {
                              height: 240,
                            },
                            legend: {
                              show: false,
                            },
                          },
                        },
                      ],
                      legend: {
                        position: "bottom",
                        offsetY: 0,
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
          <Col lg="6" xl="4">
            <Card>
              <CardHeader>
                <h5> Gradient Donut Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[44, 55, 41, 17]}
                    type={"donut"}
                    height={340}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 340,
                        type: "donut",
                      },
                      colors: [
                        "#8973ea",
                        "#626263",
                        "#147534",
                        "#e90bc4",
                        "#2e5ce2",
                      ],
                      responsive: [
                        {
                          breakpoint: 480,
                          options: {
                            chart: {
                              width: 200,
                            },
                            legend: {
                              position: "bottom",
                            },
                          },
                        },
                      ],
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
          <Col lg="6" xl="4">
            <Card>
              <CardHeader>
                <h5> Patterned Donut Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[44, 55, 41, 17, 15]}
                    type="donut"
                    height={380}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 380,
                        type: "donut",
                        dropShadow: {
                          enabled: true,
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
                      plotOptions: {
                        pie: {
                          donut: {
                            labels: {
                              show: true,
                              total: {
                                showAlways: true,
                                show: true,
                              },
                            },
                          },
                        },
                      },
                      labels: ["Comedy", "Action", "SciFi", "Drama", "Horror"],
                      dataLabels: {
                        dropShadow: {
                          enabled: true,
                          blur: 3,
                          opacity: 0.8,
                        },
                      },
                      fill: {
                        type: "pattern",
                        opacity: 1,
                        pattern: {
                          style: [
                            "verticalLines",
                            "squares",
                            "horizontalLines",
                            "circles",
                            "slantedLines",
                          ],
                        },
                      },
                      states: {
                        hover: {
                          filter: {
                            type: "none",
                          },
                        },
                      },
                      theme: {
                        palette: "palette2",
                      },
                      responsive: [
                        {
                          breakpoint: 480,
                          options: {
                            chart: {
                              width: 200,
                            },
                            legend: {
                              position: "bottom",
                            },
                          },
                        },
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col lg="6" xl="4">
            <Card className=" equal-card">
              <CardHeader>
                <h5> Pie Chart with Image fill</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[44, 33, 54, 45]}
                    type={"pie"}
                    height={380}
                    options={{
                      chart: {
                        height: 340,
                        type: "pie",
                      },
                      colors: ["#93C3EE", "#E5C6A0", "#669DB5", "#94A74A"],
                      fill: {
                        type: "image",
                        opacity: 0.85,
                        image: {
                          src: [
                            "/images/blog/03.jpg",
                            "/images/blog/03.jpg",
                            "/images/blog/03.jpg",
                            "/images/blog/03.jpg",
                          ],
                          width: 25,
                          height: 25,
                        },
                      },
                      stroke: {
                        width: 4,
                      },
                      dataLabels: {
                        enabled: true,
                        style: {
                          colors: ["#111"],
                        },
                        background: {
                          enabled: true,
                          foreColor: "#fff",
                          borderWidth: 0,
                        },
                      },
                      responsive: [
                        {
                          breakpoint: 480,
                          options: {
                            chart: {
                              width: 200,
                            },
                            legend: {
                              position: "bottom",
                            },
                          },
                        },
                      ],
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

export default PieChartPage;
