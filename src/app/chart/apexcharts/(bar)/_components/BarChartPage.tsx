"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

type FormatterFunction = (value: number) => string;

const BarChartPage = () => {
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
          mainTitle="Bar"
          title="Chart"
          path={["Apexcharts", "Bar"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5> Basic Bar Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        data: [
                          400, 430, 448, 470, 540, 580, 690, 1100, 1200, 1380,
                        ],
                      },
                    ]}
                    type={"bar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "bar",
                        height: 350,
                      },
                      colors: ["#2e5ce2"],
                      plotOptions: {
                        bar: {
                          borderRadius: 4,
                          borderRadiusApplication: "end",
                          horizontal: true,
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      xaxis: {
                        categories: [
                          "South Korea",
                          "Canada",
                          "United Kingdom",
                          "Netherlands",
                          "Italy",
                          "France",
                          "Japan",
                          "United States",
                          "China",
                          "Germany",
                        ],
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
            <div className="card equal-card">
              <CardHeader>
                <h5> Patterned Bar Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Marine Sprite",
                        data: [44, 55, 41, 37, 22, 43, 21],
                      },
                      {
                        name: "Striking Calf",
                        data: [53, 32, 33, 52, 13, 43, 32],
                      },
                      {
                        name: "Tank Picture",
                        data: [12, 17, 11, 9, 15, 11, 20],
                      },
                      {
                        name: "Bucket Slope",
                        data: [9, 7, 5, 8, 6, 9, 4],
                      },
                    ]}
                    type={"bar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "bar",
                        height: 350,
                        stacked: true,
                        dropShadow: {
                          enabled: true,
                          blur: 1,
                          opacity: 0.25,
                        },
                      },
                      colors: ["#8973ea", "#147534", "#e90bc4", "#eaea4f"],
                      plotOptions: {
                        bar: {
                          horizontal: true,
                          barHeight: "60%",
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        width: 2,
                      },

                      xaxis: {
                        categories: [2008, 2009, 2010, 2011, 2012, 2013, 2014],
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },

                      fill: {
                        type: "pattern",
                        opacity: 1,
                        pattern: {
                          style: [
                            "circles",
                            "slantedLines",
                            "verticalLines",
                            "horizontalLines",
                          ], // string or array of strings
                        },
                      },
                      states: {
                        hover: {
                          filter: {
                            type: "none",
                          },
                        },
                      },
                      legend: {
                        position: "right",
                        offsetY: 40,
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
                      },
                      tooltip: {
                        shared: false,
                        y: {
                          formatter: function (val: string) {
                            return val + "K";
                          },
                        },
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
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5> Stacked Bar Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Marine Sprite",
                        data: [44, 55, 41, 37, 22, 43, 21],
                      },
                      {
                        name: "Striking Calf",
                        data: [53, 32, 33, 52, 13, 43, 32],
                      },
                      {
                        name: "Tank Picture",
                        data: [12, 17, 11, 9, 15, 11, 20],
                      },
                      {
                        name: "Bucket Slope",
                        data: [9, 7, 5, 8, 6, 9, 4],
                      },
                      {
                        name: "Reborn Kid",
                        data: [25, 12, 19, 32, 25, 24, 10],
                      },
                    ]}
                    type={"bar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "bar",
                        height: 350,
                        stacked: true,
                      },
                      colors: [
                        "#8973ea",
                        "#626263",
                        "#147534",
                        "#e90bc4",
                        "#eaea4f",
                      ],
                      plotOptions: {
                        bar: {
                          horizontal: true,
                          dataLabels: {
                            total: {
                              enabled: true,
                              offsetX: 0,
                              style: {
                                fontSize: "13px",
                                fontWeight: 900,
                              },
                            },
                          },
                        },
                      },
                      stroke: {
                        width: 1,
                        colors: ["#fff"],
                      },
                      xaxis: {
                        categories: [2008, 2009, 2010, 2011, 2012, 2013, 2014],
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      yaxis: {
                        title: {
                          text: undefined,
                        },
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      fill: {
                        opacity: 1,
                      },
                      legend: {
                        position: "top",
                        horizontalAlign: "left",
                        offsetX: 40,
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
                      },
                      tooltip: {
                        x: {
                          show: false,
                        },
                        style: {
                          fontSize: "16px",
                        },
                        y: {
                          formatter: function (val: string) {
                            return val + "K";
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
                <h5> Grouped Stacked Bars</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Q1 Budget",
                        group: "budget",
                        data: [44000, 55000, 41000, 67000, 22000],
                      },
                      {
                        name: "Q1 Actual",
                        group: "actual",
                        data: [48000, 50000, 40000, 65000, 25000],
                      },
                      {
                        name: "Q2 Budget",
                        group: "budget",
                        data: [13000, 36000, 20000, 8000, 13000],
                      },
                      {
                        name: "Q2 Actual",
                        group: "actual",
                        data: [20000, 40000, 25000, 10000, 12000],
                      },
                    ]}
                    type={"bar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        stacked: true,
                      },
                      colors: ["#2e5ce2", "#e90bc4"],
                      stroke: {
                        width: 1,
                        colors: ["#fff"],
                      },
                      dataLabels: {
                        enabled: true,
                        formatter: function (val: number) {
                          return (val / 1000).toFixed(1) + "K";
                        } as unknown as FormatterFunction,
                      },
                      plotOptions: {
                        bar: {
                          horizontal: true,
                        },
                      },
                      xaxis: {
                        categories: [
                          "Online advertising",
                          "Sales Training",
                          "Print advertising",
                          "Catalogs",
                          "Meetings",
                        ],
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },

                      fill: {
                        opacity: 1,
                      },

                      legend: {
                        position: "top",
                        horizontalAlign: "left",
                      },
                      yaxis: {
                        labels: {
                          style: {
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
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

export default BarChartPage;
