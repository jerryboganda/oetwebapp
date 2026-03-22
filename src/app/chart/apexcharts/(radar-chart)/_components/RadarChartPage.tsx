"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const RadarChartPage = () => {
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
          mainTitle="Radar"
          title="Chart"
          path={["Apexcharts", "Radar"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col sm="12" md="6" xl="6">
            <Card>
              <CardHeader>
                <h5> Basic Radar Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Series 1",
                        data: [80, 50, 30, 40, 100, 20],
                      },
                    ]}
                    type={"radar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "radar",
                      },
                      colors: [getLocalStorageItem("color-primary", "#8973ea")],
                      xaxis: {
                        categories: [
                          "January",
                          "February",
                          "March",
                          "April",
                          "May",
                          "June",
                        ],
                      },
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6" xl="6">
            <Card>
              <CardHeader>
                <h5> Radar Chart – Multiple Series</h5>
              </CardHeader>
              <CardBody>
                {" "}
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Series 1",
                        data: [80, 50, 30, 40, 100, 20],
                      },
                      {
                        name: "Series 2",
                        data: [20, 30, 40, 80, 20, 80],
                      },
                      {
                        name: "Series 3",
                        data: [44, 76, 78, 13, 43, 10],
                      },
                    ]}
                    type={"radar"}
                    height={350}
                    options={{
                      chart: {
                        height: 350,
                        type: "radar",
                        dropShadow: {
                          enabled: true,
                          blur: 1,
                          left: 1,
                          top: 1,
                        },
                      },
                      stroke: {
                        width: 2,
                      },
                      fill: {
                        opacity: 0.1,
                      },
                      markers: {
                        size: 0,
                      },
                      xaxis: {
                        categories: [
                          "2011",
                          "2012",
                          "2013",
                          "2014",
                          "2015",
                          "2016",
                        ],
                      },
                      colors: [
                        getLocalStorageItem("color-secondary", "#626263"),
                        "#147534",
                        "#e90bc4",
                      ],
                    }}
                  />
                ) : (
                  <Loading />
                )}
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6" xl="6">
            <Card>
              <CardHeader>
                <h5> Radar Chart – Polygon Fill</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Series 1",
                        data: [20, 100, 40, 30, 50, 80, 33],
                      },
                    ]}
                    type="radar"
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "radar",
                      },
                      dataLabels: {
                        enabled: true,
                      },
                      plotOptions: {
                        radar: {
                          size: 140,
                          polygons: {
                            strokeColors: "#e9e9e9",
                          },
                        },
                      },
                      colors: ["#F9D249"],
                      markers: {
                        size: 4,
                        colors: ["#fff"],
                        strokeColors: "#eaea4f",
                        strokeWidth: 2,
                      },
                      tooltip: {
                        y: {
                          formatter: function (val: number): string {
                            return val.toString();
                          },
                        },
                      },
                      xaxis: {
                        categories: [
                          "Sunday",
                          "Monday",
                          "Tuesday",
                          "Wednesday",
                          "Thursday",
                          "Friday",
                          "Saturday",
                        ],
                      },
                      yaxis: {
                        tickAmount: 7,
                        labels: {
                          formatter: function (val: number, i: number): string {
                            if (i % 2 === 0) {
                              return val.toString();
                            } else {
                              return "";
                            }
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

export default RadarChartPage;
