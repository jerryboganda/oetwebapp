"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem, generateData } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const BubblePage = () => {
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
          mainTitle="Bubble"
          title="Chart"
          path={["Apexcharts", "Bubble"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5> Simple Bubble Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Bubble1",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "Bubble2",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "Bubble3",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "Bubble4",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                    ]}
                    type={"bubble"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "bubble",
                        zoom: {
                          enabled: false,
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      fill: {
                        opacity: 0.8,
                      },
                      title: {
                        text: "",
                      },
                      xaxis: {
                        tickAmount: 12,
                        type: "datetime",
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      colors: [
                        getLocalStorageItem("color-primary", "#8973EA"),
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#147534",
                        "#E90BC4",
                      ],
                      yaxis: {
                        max: 70,
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
                <h5>3D Bubble Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "3D Bubble 1",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "3D Bubble 2",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "3D Bubble 3",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                      {
                        name: "3D Bubble 4",
                        data: generateData(
                          new Date("11 Feb 2017 GMT").getTime(),
                          20,
                          {
                            min: 10,
                            max: 60,
                          }
                        ),
                      },
                    ]}
                    type={"bubble"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "bubble",
                        zoom: {
                          enabled: false,
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      fill: {
                        type: "gradient",
                      },
                      title: {
                        text: "",
                      },
                      xaxis: {
                        tickAmount: 12,
                        type: "datetime",
                        labels: {
                          rotate: 0,
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },

                      colors: ["#8973ea", "#147534", "#e90bc4", "#2e5ce2"],
                      yaxis: {
                        max: 70,
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      theme: {
                        palette: "palette2",
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
        </Row>
      </Container>
    </div>
  );
};

export default BubblePage;
