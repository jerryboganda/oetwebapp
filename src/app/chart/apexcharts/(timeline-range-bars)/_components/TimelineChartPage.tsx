"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { ApexChartsData } from "@/Data/Charts/ApexCharts";
import { getLocalStorageItem } from "@/_helper";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

type ApexTooltipCustomContext = {
  ctx: { w: any };
  series: number[][];
  seriesIndex: number;
  dataPointIndex: number;
  y1: number;
  y2: number;
};

const TimelineChartPage = () => {
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
          mainTitle="Timeline & Range Charts"
          title="Chart"
          path={["Apexcharts", "Timeline & Range Charts"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5> Basic Timeline Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={[
                      {
                        data: [
                          {
                            x: "Code",
                            y: [
                              new Date("2019-03-02").getTime(),
                              new Date("2019-03-04").getTime(),
                            ],
                          },
                          {
                            x: "Test",
                            y: [
                              new Date("2019-03-04").getTime(),
                              new Date("2019-03-08").getTime(),
                            ],
                          },
                          {
                            x: "Validation",
                            y: [
                              new Date("2019-03-08").getTime(),
                              new Date("2019-03-12").getTime(),
                            ],
                          },
                          {
                            x: "Deployment",
                            y: [
                              new Date("2019-03-12").getTime(),
                              new Date("2019-03-18").getTime(),
                            ],
                          },
                        ],
                      },
                    ]}
                    type={"rangeBar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "rangeBar",
                        zoom: {
                          enabled: false,
                        },
                      },
                      plotOptions: {
                        bar: {
                          horizontal: true,
                        },
                      },
                      xaxis: {
                        type: "datetime",
                        labels: {
                          rotate: -30,
                          rotateAlways: true,
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
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      colors: [getLocalStorageItem("color-primary", "#8973ea")],
                      responsive: [
                        {
                          breakpoint: 768,
                          options: {
                            chart: {
                              height: 280,
                            },
                          },
                        },
                      ],
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
                <h5>Advanced Timeline (Multiple range)</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.AdvanceTimeline}
                    type={"rangeBar"}
                    height={450}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 450,
                        type: "rangeBar",
                        zoom: {
                          enabled: false,
                        },
                      },
                      plotOptions: {
                        bar: {
                          horizontal: true,
                          barHeight: "80%",
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
                        type: "datetime",
                        labels: {
                          rotate: -30,
                          rotateAlways: true,
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      colors: [
                        "#282632",
                        getLocalStorageItem("color-primary", "#8973ea"),
                      ],
                      stroke: {
                        width: 1,
                      },
                      fill: {
                        type: "solid",
                        opacity: 0.6,
                      },
                      legend: {
                        position: "top",
                        horizontalAlign: "left",
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
                <h5> Timeline – Grouped Rows</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.AdvanceGroupedRows}
                    type={"rangeBar"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        height: 350,
                        type: "rangeBar",
                        zoom: {
                          enabled: false,
                        },
                      },
                      plotOptions: {
                        bar: {
                          horizontal: true,
                          barHeight: "50%",
                          rangeBarGroupRows: true,
                        },
                      },
                      colors: [
                        "#8973ea",
                        "#147534",
                        "#eaea4f",
                        "#2e5ce2",
                        "#282632",
                        "#8973ea",
                        "#626263",
                        "#e90bc4",
                        "#eaea4f",
                        "#2e5ce2",
                      ],
                      fill: {
                        type: "solid",
                      },
                      xaxis: {
                        type: "datetime",
                        labels: {
                          rotate: -30,
                          rotateAlways: true,
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
                      legend: {
                        position: "right",
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
                        custom: function (opts: ApexTooltipCustomContext) {
                          const fromYear = new Date(opts.y1).getFullYear();
                          const toYear = new Date(opts.y2).getFullYear();

                          const w = opts.ctx.w;
                          const label = w.globals.labels[opts.dataPointIndex];
                          const seriesName = w.config.series[opts.seriesIndex]
                            .name
                            ? w.config.series[opts.seriesIndex].name
                            : "";
                          const color = w.globals.colors[opts.seriesIndex];

                          return (
                            '<div class="apexcharts-tooltip-rangebar">' +
                            '<div> <span class="series-name" style="color: ' +
                            color +
                            '">' +
                            (seriesName ? seriesName : "") +
                            "</span></div>" +
                            '<div> <span class="category">' +
                            label +
                            ' </span> <span class="value start-value">' +
                            fromYear +
                            '</span> <span class="separator">-</span> <span class="value end-value">' +
                            toYear +
                            "</span></div>" +
                            "</div>"
                          );
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
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default TimelineChartPage;
