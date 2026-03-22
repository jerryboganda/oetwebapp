"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import { ApexChartsData } from "@/Data/Charts/ApexCharts";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const ColumnPage = () => {
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
          mainTitle="column"
          title="Chart"
          path={["Apexcharts", "column"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5> Basic Column Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Net Profit",
                        data: [44, 55, 57, 56, 61, 58, 63, 60, 66],
                      },
                      {
                        name: "Revenue",
                        data: [76, 85, 101, 98, 87, 105, 91, 114, 94],
                      },
                      {
                        name: "Free Cash Flow",
                        data: [35, 41, 36, 26, 45, 48, 52, 53, 41],
                      },
                    ]}
                    type={"bar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                      },
                      plotOptions: {
                        bar: {
                          horizontal: false,
                          columnWidth: "55%",
                        },
                      },
                      colors: [
                        getLocalStorageItem("color-primary", "#8973ea"),
                        getLocalStorageItem("color-secondary", "#147534"),
                        "#e90bc4",
                      ],
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        show: true,
                        width: 2,
                        colors: ["transparent"],
                      },
                      xaxis: {
                        categories: [
                          "Feb",
                          "Mar",
                          "Apr",
                          "May",
                          "Jun",
                          "Jul",
                          "Aug",
                          "Sep",
                          "Oct",
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
                      fill: {
                        opacity: 1,
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
                      },
                      tooltip: {
                        y: {
                          formatter: function (val: number) {
                            return `$${val} thousands`;
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
                <h5> Dumbbell Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Inflation",
                        data: [
                          {
                            x: "2008",
                            y: [2800, 4500],
                          },
                          {
                            x: "2009",
                            y: [3200, 4100],
                          },
                          {
                            x: "2010",
                            y: [2950, 7800],
                          },
                          {
                            x: "2011",
                            y: [3000, 4600],
                          },
                          {
                            x: "2012",
                            y: [3500, 4100],
                          },
                          {
                            x: "2013",
                            y: [4500, 6500],
                          },
                          {
                            x: "2014",
                            y: [4100, 5600],
                          },
                        ],
                      },
                    ]}
                    height={350}
                    type="rangeBar"
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                      },
                      plotOptions: {
                        bar: {
                          isDumbbell: true,
                          columnWidth: 25,
                          dumbbellColors: [["#000000", "#000000"]],
                        },
                      },
                      legend: {
                        show: true,
                        showForSingleSeries: true,
                        position: "top",
                        horizontalAlign: "right",
                        customLegendItems: ["Product A", "Product B"],
                      },
                      colors: ["#eaea4f", "#147534"],
                      fill: {
                        type: "gradient",
                        gradient: {
                          type: "vertical",
                          gradientToColors: ["#e90bc4"],
                          inverseColors: true,
                          stops: [0, 100],
                        },
                      },
                      grid: {
                        show: true,
                        borderColor: "rgba(var(--dark),.2)",
                        strokeDashArray: 2,
                        xaxis: {
                          lines: {
                            show: true,
                          },
                        },
                        yaxis: {
                          lines: {
                            show: false,
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
                        tickPlacement: "on",
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
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5> Column Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        name: "Servings",
                        data: [
                          44, 55, 41, 67, 22, 43, 21, 33, 45, 31, 87, 65, 35,
                        ],
                      },
                    ]}
                    height={350}
                    type="bar"
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                      },
                      plotOptions: {
                        bar: {
                          borderRadius: 10,
                          columnWidth: "50%",
                        },
                      },
                      dataLabels: {
                        enabled: false,
                      },
                      stroke: {
                        width: 0,
                      },
                      xaxis: {
                        labels: {
                          rotate: -45,
                          rotateAlways: true,
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                        categories: [
                          "Apples",
                          "Oranges",
                          "Strawberries",
                          "Pineapples",
                          "Mangoes",
                          "Bananas",
                          "Blackberries",
                          "Pears",
                          "Watermelons",
                          "Cherries",
                          "Pomegranates",
                          "Tangerines",
                          "Papayas",
                        ],
                        tickPlacement: "on",
                      },
                      colors: ["#2e5ce2"],
                      fill: {
                        type: "gradient",
                        gradient: {
                          shade: "light",
                          type: "horizontal",
                          shadeIntensity: 0.25,
                          gradientToColors: undefined,
                          inverseColors: true,
                          opacityFrom: 0.85,
                          opacityTo: 0.85,
                          stops: [50, 0, 100],
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
                        row: {
                          colors: ["#fff", "#f2f2f2"],
                        },
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
                <h5> Column with Markers</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.ColumnChartWithMarkers}
                    type="bar"
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "bar",
                      },
                      plotOptions: {
                        bar: {
                          columnWidth: "60%",
                          dataLabels: {
                            position: "top",
                          },
                        },
                      },
                      colors: ["#eaea4f"],
                      dataLabels: {
                        enabled: false,
                      },
                      legend: {
                        show: true,
                        showForSingleSeries: true,
                        customLegendItems: ["Actual", "Expected"],
                        markers: {
                          fillColors: ["#eaea4f", "#fac253"],
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

export default ColumnPage;
