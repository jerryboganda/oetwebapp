"use client";
import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getLocalStorageItem } from "@/_helper";
import { ApexChartsData } from "@/Data/Charts/ApexCharts";
import Loading from "@/app/loading";
import { IconChartPie } from "@tabler/icons-react";

const CandlestickChartPage = () => {
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
          mainTitle="Candlestick"
          title="Chart"
          path={["Apexcharts", "Candlestick"]}
          Icon={IconChartPie}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Basic Candlestick Chart</h5>
              </CardHeader>
              <CardBody>
                {ApexCharts ? (
                  <ApexCharts
                    series={ApexChartsData.BasicCandlestickChart}
                    type={"candlestick"}
                    height={350}
                    options={{
                      chart: {
                        fontFamily: "Montserrat, system-ui",
                        type: "candlestick",
                        height: 350,
                      },
                      title: {
                        text: "CandleStick Chart",
                        align: "left",
                      },
                      plotOptions: {
                        candlestick: {
                          colors: {
                            upward:
                              getLocalStorageItem("color-primary", "#8973ea") ||
                              "#8973ea",
                            downward:
                              getLocalStorageItem(
                                "color-secondary",
                                "#626263"
                              ) || "#626263",
                          },
                        },
                      },
                      xaxis: {
                        type: "datetime",
                        labels: {
                          style: {
                            colors: [],
                            fontSize: "14px",
                            fontWeight: 500,
                          },
                        },
                      },
                      yaxis: {
                        tooltip: {
                          enabled: true,
                        },
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
        </Row>
      </Container>
    </div>
  );
};

export default CandlestickChartPage;
